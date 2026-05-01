using Microsoft.Win32;
using ScrollShot.Core;
using ScrollShot.Helpers;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace ScrollShot;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<WindowInfo> _windows = [];
    private readonly List<string> _log = [];
    private CancellationTokenSource? _captureCancellation;
    private bool _isCapturing;

    public ObservableCollection<WindowInfo> Windows => _windows;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshWindows();
        UpdateSpeedText();
        AddLog("Idle");
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshWindows();

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

        RefreshWindows();
        WindowInfo? match = _windows.FirstOrDefault(window => window.Handle == picked.Handle);
        if (match is null)
        {
            _windows.Insert(0, picked);
            match = picked;
        }

        WindowCombo.SelectedItem = match;
        AddLog($"Picked {match.Title}");
        StatusText.Text = "Idle";
    }

    private async void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCapturing)
        {
            _captureCancellation?.Cancel();
            AddLog("Cancel requested");
            return;
        }

        if (WindowCombo.SelectedItem is not WindowInfo target)
        {
            MessageBox.Show(this, "Choose a target window first.", "ScrollShot", MessageBoxButton.OK, MessageBoxImage.Information);
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

        try
        {
            CaptureOptions options = new(target.Handle, target.IsBrowser, GetDelayFromSlider());
            Progress<CaptureProgress> progress = new(UpdateProgress);

            List<CapturedFrame> frames = await ScrollCapture.CaptureAsync(options, progress, token);
            if (frames.Count == 0)
            {
                throw new InvalidOperationException("No frames were captured.");
            }

            StatusText.Text = "Stitching";
            AddLog($"Stitching {frames.Count} frame(s)");

            CapturedFrame stitched = await Task.Run(() => ImageStitcher.Stitch(frames, progress, token), token);
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
            MessageBox.Show(this, ex.Message, "ScrollShot", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetCapturingState(false);
            _captureCancellation?.Dispose();
            _captureCancellation = null;
        }
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateSpeedText();

    private void RefreshWindows()
    {
        WindowInfo? current = WindowCombo.SelectedItem as WindowInfo;
        IntPtr ownHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _windows.Clear();

        foreach (WindowInfo window in WindowEnumerator.GetOpenWindows())
        {
            if (window.Handle != ownHandle)
            {
                _windows.Add(window);
            }
        }

        WindowCombo.SelectedItem = current is null
            ? _windows.FirstOrDefault(window => window.IsBrowser) ?? _windows.FirstOrDefault()
            : _windows.FirstOrDefault(window => window.Handle == current.Handle) ?? _windows.FirstOrDefault();

        AddLog($"Found {_windows.Count} window(s)");
    }

    private SaveFileDialog CreateSaveDialog()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        return new SaveFileDialog
        {
            Title = "Save scrolling screenshot",
            InitialDirectory = desktop,
            FileName = $"ScrollShot_{stamp}.png",
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
        WindowCombo.IsEnabled = !isCapturing;
        RefreshButton.IsEnabled = !isCapturing;
        PickButton.IsEnabled = !isCapturing;
        SpeedSlider.IsEnabled = !isCapturing;
        if (isCapturing)
        {
            ProgressBar.Value = 0;
        }
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
