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
using Cursors = System.Windows.Input.Cursors;

namespace AdvGenAudioWave;

public partial class MainWindow : Window
{
    private AudioProcessor? _audioProcessor;
    private Color _waveformColor = Colors.White;
    private bool _ffmpegAvailable;

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
            var frame0 = WaveformRenderer.RenderFrame(baseWaveform, 0, 1, width, height);
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

    private void ExportApng_Click(object sender, RoutedEventArgs e)
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

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var barCount = WaveformRenderer.ComputeBarCount(width);
            var peaks = _audioProcessor.ExtractPeaks(barCount);
            WaveformRenderer.ExportApng(
                dialog.FileName, peaks, width, height, _waveformColor,
                frameCount, _audioProcessor.TotalDurationMs);
            MessageBox.Show($"Saved to:\n{dialog.FileName}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void ExportMov_Click(object sender, RoutedEventArgs e)
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
            Filter = "QuickTime Movie|*.mov",
            DefaultExt = ".mov",
            FileName = "waveform"
        };
        if (dialog.ShowDialog() != true) return;

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var barCount = WaveformRenderer.ComputeBarCount(width);
            var peaks = _audioProcessor.ExtractPeaks(barCount);
            WaveformRenderer.ExportMov(
                dialog.FileName, peaks, width, height, _waveformColor,
                frameCount, _audioProcessor.TotalDurationSeconds);
            MessageBox.Show($"Saved to:\n{dialog.FileName}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            // Unwrap FFMpegCore exceptions to surface ffmpeg stderr output
            var detail = ex.InnerException?.Message ?? ex.Message;
            MessageBox.Show($"MOV export failed:\n\n{detail}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
}
