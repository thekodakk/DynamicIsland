using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamikIsland
{
    public partial class MainWindow : Window
    {
        private const double WIDTH_COMPACT   = 360.0;
        private const double HEIGHT_COMPACT  = 38.0;

        private const double WIDTH_EXPANDED  = 460.0;
        private const double HEIGHT_EXPANDED = 150.0;

        private bool _expanded = false;
        private int  _currentMode = 0; // 0: Sistem, 1: Ağ, 2: Disk & Uptime
        private const int TOTAL_MODES = 3;

        private Storyboard? _sbExpand;
        private Storyboard? _sbCollapse;
        private DispatcherTimer? _timer;

        private PerformanceCounter? _cpuCounter;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            PlaceAtTopCenter();
            RegisterStartupSafe();

            _sbExpand   = (Storyboard)FindResource("ExpandSB");
            _sbCollapse = (Storyboard)FindResource("CollapseSB");

            _sbCollapse.Completed += (_, _) =>
            {
                if (!_expanded)
                {
                    ReCenter(WIDTH_COMPACT, HEIGHT_COMPACT);
                }
            };

            InitPerfCounterSafe();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTimerTick;
            _timer.Start();

            UpdateModeUI();
            UpdateHardwareStats();
        }

        private void InitPerfCounterSafe()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // First call primes value
            }
            catch
            {
                _cpuCounter = null;
            }
        }

        private void RegisterStartupSafe()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath)) return;

                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

                if (key != null)
                {
                    key.SetValue("DynamikIsland", $"\"{exePath}\"");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Startup] Registry error: {ex.Message}");
            }
        }

        private void PlaceAtTopCenter()
        {
            double sw = SystemParameters.PrimaryScreenWidth;
            Width  = WIDTH_COMPACT;
            Height = HEIGHT_COMPACT;
            Left   = Math.Round((sw - WIDTH_COMPACT) / 2.0);
            Top    = 0;
        }

        private void ReCenter(double targetW, double targetH)
        {
            double sw = SystemParameters.PrimaryScreenWidth;
            Width  = targetW;
            Height = targetH;
            Left   = Math.Round((sw - targetW) / 2.0);
            Top    = 0;
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            UpdateHardwareStats();
        }

        private void UpdateHardwareStats()
        {
            try
            {
                // CPU
                float cpuVal = 0;
                if (_cpuCounter != null)
                {
                    cpuVal = _cpuCounter.NextValue();
                }
                P0_CpuText.Text = $"CPU Yükü: %{cpuVal:F1}";

                // RAM & Uptime & Disk
                var gcInfo = GC.GetGCMemoryInfo();
                double totalRamGb = gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0 * 1024.0);
                if (totalRamGb <= 0) totalRamGb = 16.0;

                double usedRamGb = gcInfo.MemoryLoad > 0 ? (totalRamGb * gcInfo.MemoryLoad / 100.0) : 8.0;
                double ramPct = gcInfo.MemoryLoad > 0 ? gcInfo.MemoryLoad : 50.0;

                P0_RamText.Text = $"RAM: {usedRamGb:F1} / {totalRamGb:F1} GB (%{ramPct:F0})";

                // Compact summary
                CompactSummaryText.Text = $"CPU %{cpuVal:F0} · RAM %{ramPct:F0}";

                // Disk
                DriveInfo cDrive = DriveInfo.GetDrives().FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase))
                                   ?? DriveInfo.GetDrives().FirstOrDefault();

                if (cDrive != null && cDrive.IsReady)
                {
                    double freeGb  = cDrive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double totalGb = cDrive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freePct = (freeGb / totalGb) * 100.0;
                    P2_DiskText.Text = $"C: Boş Alan: {freeGb:F1} GB / {totalGb:F1} GB (%{freePct:F0} Boş)";
                }

                // Uptime
                TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                P2_UptimeText.Text = uptime.Days > 0
                    ? $"Sistem Açık: {uptime.Days} Gün {uptime.Hours} Saat {uptime.Minutes} Dk"
                    : $"Sistem Açık: {uptime.Hours} Saat {uptime.Minutes} Dk";

                // Top Process
                Process? topProc = Process.GetProcesses()
                    .Where(p => !string.Equals(p.ProcessName, "Idle", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(p => p.WorkingSet64)
                    .FirstOrDefault();

                if (topProc != null)
                {
                    P0_ProcText.Text = $"En Çok Tüketen: {topProc.ProcessName}.exe";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Stats] Update error: {ex.Message}");
            }
        }

        private void Island_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!_expanded)
            {
                OnMouseEnter(sender, null!);
                _currentMode = 1; // 2. moda geç ve genişlet
                UpdateModeUI();
                e.Handled = true;
                return;
            }

            if (e.Delta < 0)
            {
                _currentMode = (_currentMode + 1) % TOTAL_MODES;
            }
            else if (e.Delta > 0)
            {
                _currentMode = (_currentMode - 1 + TOTAL_MODES) % TOTAL_MODES;
            }

            UpdateModeUI();
            e.Handled = true;
        }

        private void UpdateModeUI()
        {
            // Update Slot Badge and Header based on selected mode
            switch (_currentMode)
            {
                case 0: // Sistem Özeti
                    SlotIcon.Text = "⚡";
                    SlotTag.Text = "Sistem";
                    ExpandedSlotIcon.Text = "⚡";
                    ExpandedSlotTitle.Text = "SİSTEM ÖZETİ";

                    Page0Content.Visibility = Visibility.Visible;
                    Page1Content.Visibility = Visibility.Collapsed;
                    Page2Content.Visibility = Visibility.Collapsed;
                    break;

                case 1: // Ağ ve İnternet
                    SlotIcon.Text = "🌐";
                    SlotTag.Text = "İnternet";
                    ExpandedSlotIcon.Text = "🌐";
                    ExpandedSlotTitle.Text = "AĞ & İNTERNET";

                    Page0Content.Visibility = Visibility.Collapsed;
                    Page1Content.Visibility = Visibility.Visible;
                    Page2Content.Visibility = Visibility.Collapsed;
                    break;

                case 2: // Disk & Sistem
                    SlotIcon.Text = "💾";
                    SlotTag.Text = "Disk";
                    ExpandedSlotIcon.Text = "💾";
                    ExpandedSlotTitle.Text = "DİSK & SİSTEM";

                    Page0Content.Visibility = Visibility.Collapsed;
                    Page1Content.Visibility = Visibility.Collapsed;
                    Page2Content.Visibility = Visibility.Visible;
                    break;
            }

            // Dot indicators (• o o)
            Dot0.Text = _currentMode == 0 ? "●" : "○";
            Dot0.Foreground = _currentMode == 0 ? (Brush)new BrushConverter().ConvertFrom("#C084FC")! : (Brush)new BrushConverter().ConvertFrom("#50FFFFFF")!;

            Dot1.Text = _currentMode == 1 ? "●" : "○";
            Dot1.Foreground = _currentMode == 1 ? (Brush)new BrushConverter().ConvertFrom("#C084FC")! : (Brush)new BrushConverter().ConvertFrom("#50FFFFFF")!;

            Dot2.Text = _currentMode == 2 ? "●" : "○";
            Dot2.Foreground = _currentMode == 2 ? (Brush)new BrushConverter().ConvertFrom("#C084FC")! : (Brush)new BrushConverter().ConvertFrom("#50FFFFFF")!;
        }

        private void Dot0_Click(object sender, MouseButtonEventArgs e) { _currentMode = 0; UpdateModeUI(); }
        private void Dot1_Click(object sender, MouseButtonEventArgs e) { _currentMode = 1; UpdateModeUI(); }
        private void Dot2_Click(object sender, MouseButtonEventArgs e) { _currentMode = 2; UpdateModeUI(); }

        private void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (_expanded) return;
            _expanded = true;
            _sbCollapse?.Stop();
            ReCenter(WIDTH_EXPANDED, HEIGHT_EXPANDED);
            _sbExpand?.Begin();
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (!_expanded) return;
            _expanded = false;
            _sbExpand?.Stop();
            _sbCollapse?.Begin();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape) Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            base.OnClosed(e);
        }
    }
}
