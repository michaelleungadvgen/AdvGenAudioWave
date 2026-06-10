using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    internal static double ComputeFps(int frameCount, double durationSeconds)
        => Math.Max(1.0, (double)frameCount / durationSeconds);

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
        BitmapSource baseWaveform, int frameIndex, int frameCount, int imageWidth, int imageHeight)
    {
        var cursorX = ComputeCursorX(frameIndex, frameCount, imageWidth);
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawImage(baseWaveform, new Rect(0, 0, imageWidth, imageHeight));
            ctx.DrawRectangle(System.Windows.Media.Brushes.Red, null, new Rect(cursorX, 0, 2, imageHeight));
        }
        var rtb = new RenderTargetBitmap(imageWidth, imageHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    public static void ExportApng(
        string outputPath, float[] peaks, int width, int height,
        System.Windows.Media.Color barColor, int frameCount, long audioDurationMs)
    {
        var baseWaveform = RenderBaseWaveform(peaks, width, height, barColor);
        var frameDelayCs = ComputeFrameDelayCs(audioDurationMs, frameCount);

        using var collection = new MagickImageCollection();
        for (var i = 0; i < frameCount; i++)
        {
            var frame = RenderFrame(baseWaveform, i, frameCount, width, height);
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            encoder.Save(ms);
            ms.Position = 0;

            var magickFrame = new MagickImage(ms);
            magickFrame.AnimationDelay = (uint)frameDelayCs;
            collection.Add(magickFrame);
        }

        if (collection.Count > 0)
            collection[0].AnimationIterations = 0; // loop forever

        collection.Write(outputPath, MagickFormat.APng);
    }
}
