#pragma warning disable CS8019
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
#pragma warning restore CS8019

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
}
