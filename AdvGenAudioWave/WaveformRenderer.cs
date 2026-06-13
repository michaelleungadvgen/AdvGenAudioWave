using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FFMpegCore;
using ImageMagick;

namespace AdvGenAudioWave;

public static class WaveformRenderer
{
    private const int BarWidth = 3;
    private const int Gap = 1;
    private const int SlotWidth = BarWidth + Gap; // 4

    internal static int ComputeBarCount(int imageWidth)
        => imageWidth / SlotWidth;

    internal static int ComputeCursorX(int frameIndex, int frameCount, int imageWidth)
        => frameCount == 1
            ? 0
            : (int)Math.Round((double)frameIndex / (frameCount - 1) * (imageWidth - 1));

    internal static int ComputeFrameDelayCs(double durationMs, int frameCount)
        => Math.Clamp((int)Math.Round(durationMs / frameCount / 10.0), 1, 65535);

    // Number of MOV frames to render so the video length equals the audio length
    // at the chosen frame rate: frames / fps == duration. Floors at 1.
    internal static int ComputeMovFrameCount(double fps, double durationSeconds)
        => Math.Max(1, (int)Math.Round(fps * durationSeconds));

    public static BitmapSource RenderBaseWaveform(
        float[] peaks, int width, int height, System.Windows.Media.Color barColor)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(0, 0, width, height));
            var brush = new SolidColorBrush(barColor);
            brush.Freeze();
            var centerY = height / 2.0;
            for (var i = 0; i < peaks.Length; i++)
            {
                var barH = peaks[i] * centerY;
                if (barH < 0.5) continue;
                var x = i * SlotWidth;
                ctx.DrawRectangle(brush, null,
                    new Rect(x, centerY - barH, BarWidth, barH * 2));
            }
        }
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    public static BitmapSource RenderFrame(
        BitmapSource baseWaveform, int frameIndex, int frameCount,
        int imageWidth, int imageHeight,
        double envelopeScale = 1.0, bool drawCursor = true)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            var centerY = imageHeight / 2.0;
            var scale = new ScaleTransform(1, envelopeScale, 0, centerY);
            scale.Freeze();
            ctx.PushTransform(scale);
            ctx.DrawImage(baseWaveform, new Rect(0, 0, imageWidth, imageHeight));
            ctx.Pop();                       // cursor must NOT be scaled

            if (drawCursor)
            {
                var cursorX = ComputeCursorX(frameIndex, frameCount, imageWidth);
                ctx.DrawRectangle(System.Windows.Media.Brushes.Red, null,
                    new Rect(cursorX, 0, 2, imageHeight));
            }
        }
        var rtb = new RenderTargetBitmap(imageWidth, imageHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    public static void ExportApng(
        string outputPath, float[] peaks, int width, int height,
        System.Windows.Media.Color barColor, int frameCount, long audioDurationMs,
        float[] envelope, AnimationMode mode, IProgress<ExportProgress>? progress = null)
    {
        if (envelope.Length < frameCount)
            throw new ArgumentException(
                $"envelope length ({envelope.Length}) must be >= frameCount ({frameCount}).", nameof(envelope));
        var baseWaveform = RenderBaseWaveform(peaks, width, height, barColor);
        var frameDelayCs = ComputeFrameDelayCs(audioDurationMs, frameCount);
        var reportStep = Math.Max(1, frameCount / 100);

        using var collection = new MagickImageCollection();
        for (var i = 0; i < frameCount; i++)
        {
            var scale = mode == AnimationMode.CursorSweep ? 1.0 : envelope[i];
            var drawCursor = mode != AnimationMode.Pulse;
            var frame = RenderFrame(baseWaveform, i, frameCount, width, height, scale, drawCursor);
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            encoder.Save(ms);
            ms.Position = 0;

            var magickFrame = new MagickImage(ms);
            magickFrame.AnimationDelay = (uint)frameDelayCs;
            collection.Add(magickFrame);

            if (i % reportStep == 0 || i == frameCount - 1)
                progress?.Report(new ExportProgress("Rendering frames", (i + 1) / (double)frameCount));
        }

        if (collection.Count > 0)
            collection[0].AnimationIterations = 0; // loop forever

        progress?.Report(new ExportProgress("Writing APNG", double.NaN));
        collection.Write(outputPath, MagickFormat.APng);
    }

    // Returns the temp dir path so callers/tests can verify cleanup.
    public static string ExportMov(
        string outputPath, float[] peaks, int width, int height,
        System.Windows.Media.Color barColor, int frameCount, double fps,
        float[] envelope, AnimationMode mode, IProgress<ExportProgress>? progress = null)
    {
        if (envelope.Length < frameCount)
            throw new ArgumentException(
                $"envelope length ({envelope.Length}) must be >= frameCount ({frameCount}).", nameof(envelope));
        var baseWaveform = RenderBaseWaveform(peaks, width, height, barColor);
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var reportStep = Math.Max(1, frameCount / 100);

        try
        {
            for (var i = 0; i < frameCount; i++)
            {
                var scale = mode == AnimationMode.CursorSweep ? 1.0 : envelope[i];
                var drawCursor = mode != AnimationMode.Pulse;
                var frame = RenderFrame(baseWaveform, i, frameCount, width, height, scale, drawCursor);
                var framePath = Path.Combine(tempDir, $"frame{i:D4}.png");
                using var fs = new FileStream(framePath, FileMode.Create);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(frame));
                encoder.Save(fs);

                if (i % reportStep == 0 || i == frameCount - 1)
                    progress?.Report(new ExportProgress("Rendering frames", (i + 1) / (double)frameCount));
            }

            progress?.Report(new ExportProgress("Encoding video", double.NaN));
            var inputPattern = Path.Combine(tempDir, "frame%04d.png");
            FFMpegArguments
                .FromFileInput(inputPattern, false, opt =>
                    opt.WithFramerate(fps))
                .OutputToFile(outputPath, true, opt => opt
                    .WithVideoCodec("prores_ks")
                    .WithCustomArgument("-profile:v 4")
                    .WithCustomArgument("-pix_fmt yuva444p10le"))
                .ProcessSynchronously();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }

        return tempDir;
    }
}
