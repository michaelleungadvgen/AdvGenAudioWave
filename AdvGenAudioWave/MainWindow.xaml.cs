using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Brushes = System.Windows.Media.Brushes;
using SystemColors = System.Windows.SystemColors;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AdvGenAudioWave;

public partial class MainWindow : Window
{
    private AudioProcessor? _audioProcessor;
    private Color _waveformColor = Colors.White;
    private bool _ffmpegAvailable;
    private CancellationTokenSource? _exportCts;

    public MainWindow() => InitializeComponent();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _ffmpegAvailable = File.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"));
        if (!_ffmpegAvailable)
        {
            ExportMovButton.IsEnabled = false;
            ExportMovButton.ToolTip = "ffmpeg.exe not found next to the app";
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "MP3 Files|*.mp3", Title = "Select MP3 File" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            _audioProcessor = AudioProcessor.Load(dialog.FileName);
            FilePathBox.Text = dialog.FileName;
            FilePathBox.Foreground = SystemColors.ControlTextBrush;
            ExportApngButton.IsEnabled = true;
            if (_ffmpegAvailable) ExportMovButton.IsEnabled = true;
            UpdateFrameDelayLabel();
            UpdateDelayClampWarning();
            RefreshPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load MP3: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ResetAudioState();
        }
    }

    private void Settings_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        RefreshPreview();
    }

    private void Frames_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        UpdateFrameDelayLabel();
        UpdateDelayClampWarning();
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog();
        dialog.Color = System.Drawing.Color.FromArgb(
            _waveformColor.A, _waveformColor.R, _waveformColor.G, _waveformColor.B);
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _waveformColor = Color.FromArgb(
            dialog.Color.A, dialog.Color.R, dialog.Color.G, dialog.Color.B);
        ColorSwatch.Fill = new SolidColorBrush(_waveformColor);
        RefreshPreview();
    }

    private void UpdateFrameDelayLabel()
    {
        if (_audioProcessor is null || !int.TryParse(FramesBox.Text, out var fc) || fc < 1)
        {
            FrameDelayLabel.Text = "";
            return;
        }
        var delayMs = _audioProcessor.TotalDurationMs / (double)fc;
        FrameDelayLabel.Text = $"Frame delay: {delayMs:F1} ms";
    }

    private void UpdateDelayClampWarning()
    {
        if (_audioProcessor is null || !int.TryParse(FramesBox.Text, out var fc) || fc < 1)
        {
            WarningLabel.Visibility = Visibility.Collapsed;
            return;
        }
        var preclamp = _audioProcessor.TotalDurationMs / (double)fc / 10.0;
        WarningLabel.Visibility = preclamp > 65535 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshPreview()
    {
        if (_audioProcessor is null) return;
        if (!TryGetDimensions(out var width, out var height, out _)) return;

        try
        {
            var barCount = WaveformRenderer.ComputeBarCount(width);
            if (barCount < 1) return;
            var peaks = _audioProcessor.ExtractPeaks(barCount);
            var baseWaveform = WaveformRenderer.RenderBaseWaveform(peaks, width, height, _waveformColor);
            var frame0 = WaveformRenderer.RenderFrame(
                baseWaveform, 0, 1, width, height, envelopeScale: 1.0, drawCursor: false);
            PreviewImage.Source = frame0;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch { /* ignore preview failures */ }
    }

    private void ResetAudioState()
    {
        _audioProcessor = null;
        FilePathBox.Text = "No file selected";
        FilePathBox.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        ExportApngButton.IsEnabled = false;
        ExportMovButton.IsEnabled = false;
        PreviewImage.Source = null;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        FrameDelayLabel.Text = "";
        WarningLabel.Visibility = Visibility.Collapsed;
    }

    private bool TryGetDimensions(out int width, out int height, out int frameCount)
    {
        width = height = frameCount = 0;
        var ok = true;
        ok &= TryParseInput(WidthBox, 1, 10000, out width);
        ok &= TryParseInput(HeightBox, 1, 10000, out height);
        ok &= TryParseInput(FramesBox, 1, 9999, out frameCount);
        return ok;
    }

    private static bool TryParseInput(System.Windows.Controls.TextBox box, int min, int max, out int value)
    {
        if (int.TryParse(box.Text, out value) && value >= min && value <= max)
        {
            box.BorderBrush = SystemColors.ControlDarkBrush;
            return true;
        }
        box.BorderBrush = Brushes.Red;
        value = 0;
        return false;
    }

    private AnimationMode GetAnimationMode() => AnimationModeBox.SelectedIndex switch
    {
        0 => AnimationMode.CursorSweep,
        1 => AnimationMode.Pulse,
        _ => AnimationMode.CursorAndPulse,
    };

    private async void ExportApng_Click(object sender, RoutedEventArgs e)
    {
        if (_audioProcessor is null) return;
        if (!TryGetDimensions(out var width, out var height, out var frameCount))
        {
            MessageBox.Show("Fix the highlighted inputs before exporting.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Animated PNG|*.apng;*.png",
            DefaultExt = ".apng",
            FileName = "waveform"
        };
        if (dialog.ShowDialog() != true) return;

        // Capture all UI-thread state before handing off to the background thread.
        var proc = _audioProcessor;
        var path = dialog.FileName;
        var color = _waveformColor;
        var mode = GetAnimationMode();
        var durationMs = proc.TotalDurationMs;
        var progress = new Progress<ExportProgress>(OnExportProgress);
        _exportCts = new CancellationTokenSource();
        var token = _exportCts.Token;

        SetExporting(true);
        try
        {
            await RunStaAsync(() =>
            {
                var barCount = WaveformRenderer.ComputeBarCount(width);
                var peaks = proc.ExtractPeaks(barCount);
                var envelope = proc.ExtractEnvelope(frameCount);
                WaveformRenderer.ExportApng(
                    path, peaks, width, height, color,
                    frameCount, durationMs, envelope, mode, progress, token);
            });
            MessageBox.Show($"Saved to:\n{path}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested)
            {
                DeletePartialFile(path);
                MessageBox.Show("Export cancelled.", "Cancelled",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Export failed: {ex.InnerException?.Message ?? ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            SetExporting(false);
            _exportCts.Dispose();
            _exportCts = null;
        }
    }

    private async void ExportMov_Click(object sender, RoutedEventArgs e)
    {
        if (_audioProcessor is null) return;
        // MOV length is driven by FPS × audio duration, not the Frames field, so validate
        // width/height/FPS independently. '&=' (non-short-circuit) highlights every bad box.
        var ok = true;
        ok &= TryParseInput(WidthBox, 1, 10000, out var width);
        ok &= TryParseInput(HeightBox, 1, 10000, out var height);
        ok &= TryParseInput(FpsBox, 1, 60, out var fps);
        if (!ok)
        {
            MessageBox.Show("Fix the highlighted inputs before exporting.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "QuickTime Movie|*.mov",
            DefaultExt = ".mov",
            FileName = "waveform"
        };
        if (dialog.ShowDialog() != true) return;

        // Capture all UI-thread state before handing off to the background thread.
        var proc = _audioProcessor;
        var path = dialog.FileName;
        var color = _waveformColor;
        var mode = GetAnimationMode();
        var durationSeconds = proc.TotalDurationSeconds;
        var progress = new Progress<ExportProgress>(OnExportProgress);
        _exportCts = new CancellationTokenSource();
        var token = _exportCts.Token;

        SetExporting(true);
        try
        {
            await RunStaAsync(() =>
            {
                var barCount = WaveformRenderer.ComputeBarCount(width);
                var peaks = proc.ExtractPeaks(barCount);
                var movFrames = WaveformRenderer.ComputeMovFrameCount(fps, durationSeconds);
                var envelope = proc.ExtractEnvelope(movFrames);
                WaveformRenderer.ExportMov(
                    path, peaks, width, height, color,
                    movFrames, fps, envelope, mode, progress, token);
            });
            MessageBox.Show($"Saved to:\n{path}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested)
            {
                DeletePartialFile(path);
                MessageBox.Show("Export cancelled.", "Cancelled",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // Unwrap FFMpegCore exceptions to surface ffmpeg stderr output
                var detail = ex.InnerException?.Message ?? ex.Message;
                MessageBox.Show($"MOV export failed:\n\n{detail}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            SetExporting(false);
            _exportCts.Dispose();
            _exportCts = null;
        }
    }

    // Reflects the export progress (marshalled to the UI thread by Progress<T>).
    private void OnExportProgress(ExportProgress p)
    {
        ExportStatus.Text = p.Phase;
        if (double.IsNaN(p.Fraction))
        {
            ExportProgressBar.IsIndeterminate = true;
        }
        else
        {
            ExportProgressBar.IsIndeterminate = false;
            ExportProgressBar.Value = p.Fraction * 100;
        }
    }

    private void CancelExport_Click(object sender, RoutedEventArgs e)
    {
        _exportCts?.Cancel();
        CancelExportButton.IsEnabled = false;
        ExportStatus.Text = "Cancelling…";
    }

    private static void DeletePartialFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup of a partially-written file */ }
    }

    // Toggle the export UI: disable inputs and show the progress bar while exporting.
    private void SetExporting(bool exporting)
    {
        BrowseButton.IsEnabled = !exporting;
        ExportApngButton.IsEnabled = !exporting;
        ExportMovButton.IsEnabled = !exporting && _ffmpegAvailable;
        ExportProgressPanel.Visibility = exporting ? Visibility.Visible : Visibility.Collapsed;
        if (exporting)
        {
            ExportProgressBar.IsIndeterminate = false;
            ExportProgressBar.Value = 0;
            ExportStatus.Text = "Starting…";
            CancelExportButton.IsEnabled = true;
        }
    }

    // Runs the export on a dedicated STA thread (WPF rendering requires STA) without
    // blocking the UI thread, and surfaces exceptions through the returned Task.
    private static Task RunStaAsync(Action action)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }
}
