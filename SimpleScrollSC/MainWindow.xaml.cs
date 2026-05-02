using Microsoft.Win32;
using ScrollShot.Core;
using ScrollShot.Helpers;
using System.Drawing;
using System.IO;
using System.Windows;

namespace ScrollShot;

public partial class MainWindow : Window
{
    private readonly List<string> _log = [];
    private CancellationTokenSource? _captureCancellation;
    private bool _isCapturing;
    private WindowInfo? _selectedTarget;
    private Rectangle? _selectedCropRect;

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
            return;
        }

        AddLog("Area mode on");
    }

    private async void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCapturing)
        {
            _captureCancellation?.Cancel();
            AddLog("Cancel requested");
            return;
        }

        if (_selectedTarget is not WindowInfo target)
        {
            MessageBox.Show(this, "Pick a target window first.", "SimpleScrollSC", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
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

        bool manualMode = AreaModeToggle?.IsChecked == true;

        Task? overlayTask = null;
        TaskCompletionSource manualStart = new(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
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
                    throw new OperationCanceledException();
                }

                overlayTask = ManualCaptureOverlay.RunAsync(
                    this,
                    onStart: () =>
                    {
                        StatusText.Text = "Capturing";
                        AddLog("Manual capture started");
                        manualStart.TrySetResult();
                    },
                    onStop: () =>
                    {
                        AddLog("Manual stop requested");
                        _captureCancellation?.Cancel();
                    },
                    cancellationToken: token);
            }

            CaptureOptions options = new(
                target.Handle,
                target.IsBrowser,
                GetDelayFromSlider(),
                MaxFrames: manualMode ? 2000 : 160,
                CaptureMode: manualMode ? CaptureMode.ManualStop : CaptureMode.AutoUntilBottom,
                CropRect: cropRect);
            Progress<CaptureProgress> progress = new(UpdateProgress);

            if (manualMode)
            {
                StatusText.Text = "Ready";
                AddLog("Waiting for click-to-start");
                await manualStart.Task;
            }

            List<CapturedFrame> frames = await ScrollCapture.CaptureAsync(options, progress, token);
            if (frames.Count == 0)
            {
                throw new InvalidOperationException("No frames were captured.");
            }

            StatusText.Text = "Stitching";
            AddLog($"Stitching {frames.Count} frame(s)");

            CapturedFrame stitched = await Task.Run(
                () => ImageStitcher.Stitch(frames, progress, CancellationToken.None),
                CancellationToken.None);
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
            if (overlayTask is not null)
            {
                try { await overlayTask; } catch { }
            }

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
            Title = "Save scrolling screenshot",
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
        StatusText.Text = progress.Status;
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
        CaptureButton.Content = isCapturing ? "Cancel" : "Capture";
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
        0 => 80,
        2 => 300,
        _ => 150
    };

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
