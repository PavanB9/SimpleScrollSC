using Microsoft.Win32;
using ScrollShot.Core;
using ScrollShot.Helpers;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ScrollShot;

public partial class MainWindow : Window
{
    private readonly List<string> _log = [];
    private CancellationTokenSource? _captureCancellation;
    private bool _isCapturing;
    private WindowInfo? _selectedTarget;
    private Rectangle? _selectedCropRect;
    private long _captureBytesPerFrame;
    private HwndSource? _hwndSource;
    private const int HotkeyIdEscCancel = 1;
    private CaptureWindowState? _captureWindowState;

    private sealed class CaptureWindowState
    {
        public required string Title { get; init; }
        public required WindowState WindowState { get; init; }
        public required double Left { get; init; }
        public required double Top { get; init; }
        public required bool Topmost { get; init; }
    }

    private bool IsManualMode => AreaModeToggle?.IsChecked == true;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateSpeedText();
        AddLog("Idle");
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            UnregisterEscHotkey();
        }
        finally
        {
            base.OnClosed(e);
        }
    }

    private void EnsureEscHotkeyRegistered()
    {
        if (_hwndSource is not null)
        {
            return;
        }

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        // VK_ESCAPE = 0x1B
        _ = NativeMethods.RegisterHotKey(hwnd, HotkeyIdEscCancel, NativeMethods.MOD_NOREPEAT, 0x1B);
    }

    private void UnregisterEscHotkey()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            _ = NativeMethods.UnregisterHotKey(hwnd, HotkeyIdEscCancel);
        }

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam == (IntPtr)HotkeyIdEscCancel)
        {
            if (_isCapturing)
            {
                _captureCancellation?.Cancel();
                AddLog("Esc hotkey: stop requested");
                StatusText.Text = IsManualMode ? "Stopping" : "Cancelling";
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    private async void PickButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCapturing)
        {
            return;
        }

        AddLog("Pick mode started");
        StatusText.Text = "Pick a window...";

        WindowInfo? picked = await WindowPicker.PickAsync(this);
        if (picked is null)
        {
            AddLog("Pick mode cancelled");
            StatusText.Text = "Idle";
            return;
        }

        _selectedTarget = picked;
        TargetText.Text = picked.Title;
        AddLog($"Picked {picked.Title}");

        if (AreaModeToggle?.IsChecked == true)
        {
            await TrySelectAreaAsync(picked);
        }

        StatusText.Text = "Idle";
    }

    private void AreaModeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (AreaModeToggle?.IsChecked != true)
        {
            _selectedCropRect = null;
            AddLog("Area mode off");
            UpdateCaptureButtonLabel();
            return;
        }

        AddLog("Area mode on");
        UpdateCaptureButtonLabel();
    }

    private async void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCapturing)
        {
            _captureCancellation?.Cancel();
            AddLog(IsManualMode ? "Stop requested" : "Cancel requested");
            StatusText.Text = IsManualMode ? "Stopping" : "Cancelling";
            return;
        }

        if (_selectedTarget is not WindowInfo target)
        {
            MessageBox.Show(this, "Pick a target window first.", "SimpleScrollSC", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool manualMode = IsManualMode;

        Rectangle? cropRect = null;
        if (manualMode)
        {
            cropRect = _selectedCropRect;
            if (cropRect is null)
            {
                cropRect = await TrySelectAreaAsync(target);
            }

            if (cropRect is null)
            {
                return;
            }
        }

        SaveFileDialog dialog = CreateSaveDialog();
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string outputPath = dialog.FileName;
        SetCapturingState(true);
        _captureCancellation = new CancellationTokenSource();
        CancellationToken token = _captureCancellation.Token;

        _captureBytesPerFrame = 0;
        if (cropRect is not null)
        {
            _captureBytesPerFrame = (long)cropRect.Value.Width * cropRect.Value.Height * 4;
        }
        else if (NativeMethods.GetWindowRect(target.Handle, out NativeMethods.RECT bounds))
        {
            _captureBytesPerFrame = (long)bounds.Width * bounds.Height * 4;
        }

        try
        {
            CaptureOptions options = new(
                target.Handle,
                target.IsBrowser,
                GetDelayFromSlider(manualMode),
                MaxFrames: manualMode ? 2000 : 160,
                CaptureMode: manualMode ? CaptureMode.ManualStop : CaptureMode.AutoUntilBottom,
                CropRect: cropRect);
            Progress<CaptureProgress> progress = new(UpdateProgress);

            List<CapturedFrame> frames = await ScrollCapture.CaptureAsync(options, progress, token);
            if (frames.Count == 0)
            {
                if (manualMode)
                {
                    throw new OperationCanceledException();
                }

                throw new InvalidOperationException("No frames were captured.");
            }

            StatusText.Text = "Stitching";
            AddLog($"Stitching {frames.Count} frame(s)");

            CancellationToken stitchToken = manualMode ? CancellationToken.None : token;
            CapturedFrame stitched = await Task.Run(() => ImageStitcher.Stitch(frames, progress, stitchToken), stitchToken);
            string extension = Path.GetExtension(outputPath);
            ImageFormat format = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                 extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? ImageFormat.Jpeg
                : ImageFormat.Png;

            BitmapHelper.Save(stitched, outputPath, format, jpegQuality: 90);

            FileInfo file = new(outputPath);
            PreviewImage.Source = stitched.ToBitmapSource();
            PreviewTitle.Text = "Saved capture";
            PreviewDetails.Text = $"{stitched.Width:N0} x {stitched.Height:N0}px  |  {FormatBytes(file.Length)}";
            ProgressBar.Value = 100;
            StatusText.Text = "Done";
            AddLog($"Saved {outputPath}");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled";
            AddLog("Capture cancelled");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error";
            AddLog(ex.Message);
            MessageBox.Show(this, ex.Message, "SimpleScrollSC", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetCapturingState(false);
            _captureCancellation?.Dispose();
            _captureCancellation = null;
        }
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSpeedText();

    private SaveFileDialog CreateSaveDialog()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        return new SaveFileDialog
        {
            Title = "Save scrolling capture",
            InitialDirectory = desktop,
            FileName = $"SimpleScrollSC_{stamp}.png",
            DefaultExt = ".png",
            AddExtension = true,
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg;*.jpeg",
            OverwritePrompt = true
        };
    }

    private void UpdateProgress(CaptureProgress progress)
    {
        if (_isCapturing)
        {
            long estimatedBytes = _captureBytesPerFrame <= 0
                ? 0
                : _captureBytesPerFrame * Math.Max(0, progress.FrameCount);

            string suffix = estimatedBytes > 0
                ? $"  |  {progress.FrameCount} frames  |  ~{FormatBytes(estimatedBytes)} RAM"
                : $"  |  {progress.FrameCount} frames";

            suffix += IsManualMode ? "  |  Press Esc to stop" : "  |  Press Esc to cancel";

            StatusText.Text = progress.Status + suffix;
        }
        else
        {
            StatusText.Text = progress.Status;
        }
        if (progress.ProgressPercent.HasValue)
        {
            ProgressBar.Value = Math.Clamp(progress.ProgressPercent.Value, 0, 100);
        }

        if (!string.IsNullOrWhiteSpace(progress.Message))
        {
            AddLog(progress.Message);
        }
    }

    private void SetCapturingState(bool isCapturing)
    {
        _isCapturing = isCapturing;

        if (isCapturing)
        {
            EnsureEscHotkeyRegistered();
            EnterCaptureWindowState();
        }
        else
        {
            ExitCaptureWindowState();
            UnregisterEscHotkey();
        }

        UpdateCaptureButtonLabel();
        PickButton.IsEnabled = !isCapturing;
        SpeedSlider.IsEnabled = !isCapturing;
        if (AreaModeToggle is not null)
        {
            AreaModeToggle.IsEnabled = !isCapturing;
        }
        if (isCapturing)
        {
            ProgressBar.Value = 0;
        }
    }

    private void UpdateCaptureButtonLabel()
    {
        if (CaptureButton is null)
        {
            return;
        }

        if (_isCapturing)
        {
            CaptureButton.Content = IsManualMode ? "Stop (Esc)" : "Cancel (Esc)";
            return;
        }

        CaptureButton.Content = IsManualMode ? "Start" : "Capture";
    }

    private void EnterCaptureWindowState()
    {
        if (_captureWindowState is not null)
        {
            return;
        }

        _captureWindowState = new CaptureWindowState
        {
            Title = Title,
            WindowState = WindowState,
            Left = Left,
            Top = Top,
            Topmost = Topmost
        };

        Title = IsManualMode ? "SimpleScrollSC — Capturing (Esc to stop)" : "SimpleScrollSC — Capturing (Esc to cancel)";

        StatusText.Text = IsManualMode ? "Capturing — Press Esc to stop" : "Capturing — Press Esc to cancel";

        // Minimize so the app never occludes screen-based captures.
        // The global Esc hotkey still works while minimized.
        WindowState = WindowState.Minimized;
    }

    private void ExitCaptureWindowState()
    {
        if (_captureWindowState is null)
        {
            return;
        }

        CaptureWindowState saved = _captureWindowState;
        _captureWindowState = null;

        Title = saved.Title;
        Topmost = saved.Topmost;

        // Restore position safely onto a visible screen.
        (double left, double top) = ClampToVirtualScreen(saved.Left, saved.Top);
        Left = left;
        Top = top;
        WindowState = saved.WindowState;
    }

    private static (double Left, double Top) ClampToVirtualScreen(double left, double top)
    {
        // Keep at least a small portion visible so the window remains draggable.
        const double margin = 24;
        double vsLeft = SystemParameters.VirtualScreenLeft;
        double vsTop = SystemParameters.VirtualScreenTop;
        double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;

        double clampedLeft = Math.Clamp(left, vsLeft - margin, vsRight - margin);
        double clampedTop = Math.Clamp(top, vsTop - margin, vsBottom - margin);
        return (clampedLeft, clampedTop);
    }

    private void TryMoveAwayFromCaptureArea(WindowInfo target, Rectangle cropRect)
    {
        if (!NativeMethods.GetWindowRect(target.Handle, out NativeMethods.RECT bounds))
        {
            return;
        }

        Rectangle captureScreen = new(
            x: bounds.Left + cropRect.X,
            y: bounds.Top + cropRect.Y,
            width: cropRect.Width,
            height: cropRect.Height);

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        Rect captureDip = new(
            captureScreen.Left / dpi.DpiScaleX,
            captureScreen.Top / dpi.DpiScaleY,
            captureScreen.Width / dpi.DpiScaleX,
            captureScreen.Height / dpi.DpiScaleY);

        double margin = 16;
        double vsLeft = SystemParameters.VirtualScreenLeft;
        double vsTop = SystemParameters.VirtualScreenTop;
        double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;

        var candidates = new (double Left, double Top)[]
        {
            (vsLeft + margin, vsTop + margin),
            (vsRight - this.Width - margin, vsTop + margin),
            (vsLeft + margin, vsBottom - this.Height - margin),
            (vsRight - this.Width - margin, vsBottom - this.Height - margin)
        };

        Rect current = new(this.Left, this.Top, this.Width, this.Height);
        if (!current.IntersectsWith(captureDip))
        {
            return;
        }

        foreach ((double left, double top) in candidates)
        {
            Rect proposed = new(left, top, this.Width, this.Height);
            if (!proposed.IntersectsWith(captureDip))
            {
                this.Left = left;
                this.Top = top;
                AddLog("Moved app window out of capture area");
                return;
            }
        }

        AddLog("App window may cover the capture area; move it aside");
    }

    private async Task<Rectangle?> TrySelectAreaAsync(WindowInfo target)
    {
        if (!NativeMethods.GetWindowRect(target.Handle, out NativeMethods.RECT bounds))
        {
            AddLog("Unable to read target bounds for area selection");
            return null;
        }

        AddLog("Selecting capture area");
        StatusText.Text = "Select area";

        Rectangle? crop = await RegionSelector.PickCropRectAsync(this, bounds);
        if (crop is null)
        {
            AddLog("Area selection cancelled");
            StatusText.Text = "Idle";
            _selectedCropRect = null;
            return null;
        }

        _selectedCropRect = crop;
        AddLog($"Area selected: {crop.Value.Width}x{crop.Value.Height}");
        return crop;
    }

    private int GetDelayFromSlider() => (int)Math.Round(SpeedSlider.Value) switch
    {
        0 => 200,
        2 => 600,
        _ => 340
    };

    private int GetDelayFromSlider(bool manualMode)
    {
        int baseDelay = GetDelayFromSlider();
        return manualMode ? (int)Math.Round(baseDelay * 1.3) : baseDelay;
    }

    private void UpdateSpeedText()
    {
        if (SpeedText is null || SpeedSlider is null)
        {
            return;
        }

        SpeedText.Text = (int)Math.Round(SpeedSlider.Value) switch
        {
            0 => "Fast",
            2 => "Slow",
            _ => "Medium"
        };
    }

    private void AddLog(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss}  {message}";
        _log.Add(line);
        if (_log.Count > 80)
        {
            _log.RemoveAt(0);
        }

        StatusText.ToolTip = string.Join(Environment.NewLine, _log);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}
