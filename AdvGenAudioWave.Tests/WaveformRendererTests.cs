using Xunit;
using AdvGenAudioWave;
using System.Windows.Media;
using System.IO;

namespace AdvGenAudioWave.Tests;

public class WaveformRendererMathTests
{
    [Theory]
    [InlineData(1920, 480)]
    [InlineData(800, 200)]
    [InlineData(4, 1)]
    [InlineData(3, 0)]  // too narrow for one bar
    public void ComputeBarCount_ReturnsCorrectCount(int imageWidth, int expected)
        => Assert.Equal(expected, WaveformRenderer.ComputeBarCount(imageWidth));

    [Fact]
    public void ComputeCursorX_FirstFrame_ReturnsZero()
        => Assert.Equal(0, WaveformRenderer.ComputeCursorX(0, 10, 1920));

    [Fact]
    public void ComputeCursorX_LastFrame_ReturnsImageWidthMinusOne()
        => Assert.Equal(1919, WaveformRenderer.ComputeCursorX(9, 10, 1920));

    [Fact]
    public void ComputeCursorX_SingleFrame_ReturnsZero()
        => Assert.Equal(0, WaveformRenderer.ComputeCursorX(0, 1, 1920));

    [Fact]
    public void ComputeCursorX_MiddleFrame_IsApproximatelyCenter()
    {
        var x = WaveformRenderer.ComputeCursorX(5, 11, 1000);
        Assert.InRange(x, 490, 510);
    }

    [Theory]
    [InlineData(3000.0, 60, 5)]
    [InlineData(100.0, 1, 10)]
    [InlineData(0.0, 60, 1)]
    [InlineData(6_600_000.0, 1, 65535)]
    public void ComputeFrameDelayCs_ClampsCorrectly(double durationMs, int frameCount, int expected)
        => Assert.Equal(expected, WaveformRenderer.ComputeFrameDelayCs(durationMs, frameCount));

    [Fact]
    public void ComputeFps_VeryLongAudio_FloorAtOne()
        => Assert.Equal(1.0, WaveformRenderer.ComputeFps(1, 3600.0), precision: 5);

    [Fact]
    public void ComputeFps_NormalValues_IsCorrect()
        => Assert.Equal(10.0, WaveformRenderer.ComputeFps(60, 6.0), precision: 5);
}

public class WaveformRendererRenderTests
{
    private static float[] FlatPeaks(int count, float value = 0.5f)
        => Enumerable.Repeat(value, count).ToArray();

    [Fact]
    public void RenderBaseWaveform_ProducesCorrectDimensions()
    {
        StaHelper.Run(() =>
        {
            var peaks = FlatPeaks(480);
            var bmp = WaveformRenderer.RenderBaseWaveform(peaks, 1920, 200, Colors.White);
            Assert.Equal(1920, bmp.PixelWidth);
            Assert.Equal(200, bmp.PixelHeight);
        });
    }

    [Fact]
    public void RenderBaseWaveform_BackgroundIsTransparent()
    {
        StaHelper.Run(() =>
        {
            var peaks = FlatPeaks(100, 0f);
            var bmp = WaveformRenderer.RenderBaseWaveform(peaks, 400, 100, Colors.White);
            var pixels = new byte[400 * 100 * 4];
            bmp.CopyPixels(pixels, 400 * 4, 0);
            for (var i = 3; i < pixels.Length; i += 4)
                Assert.Equal(0, pixels[i]);
        });
    }

    [Fact]
    public void RenderBaseWaveform_BarsAreOpaque()
    {
        StaHelper.Run(() =>
        {
            var peaks = FlatPeaks(10, 1.0f);
            var bmp = WaveformRenderer.RenderBaseWaveform(peaks, 40, 100, Colors.White);
            var pixels = new byte[40 * 100 * 4];
            bmp.CopyPixels(pixels, 40 * 4, 0);
            var centerRow = 50;
            var rowStart = centerRow * 40 * 4;
            Assert.NotEqual(0, pixels[rowStart + 3]);   // x=0, alpha
            Assert.NotEqual(0, pixels[rowStart + 7]);   // x=1, alpha
            Assert.NotEqual(0, pixels[rowStart + 11]);  // x=2, alpha
            Assert.Equal(0, pixels[rowStart + 15]);     // x=3 (gap), alpha=0
        });
    }

    [Fact]
    public void RenderFrame_CursorAtExpectedPosition()
    {
        StaHelper.Run(() =>
        {
            var baseWaveform = WaveformRenderer.RenderBaseWaveform(
                FlatPeaks(10, 0f), 40, 20, Colors.White);
            var frame = WaveformRenderer.RenderFrame(baseWaveform, 0, 10, 40, 20);
            var pixels = new byte[40 * 20 * 4];
            frame.CopyPixels(pixels, 40 * 4, 0);
            // Pbgra32 byte order is B,G,R,A — at x=0,y=0: offset 0=B,1=G,2=R,3=A
            // Red cursor: R=255, alpha=255
            Assert.Equal(255, pixels[2]);  // R channel
            Assert.Equal(255, pixels[3]);  // alpha
        });
    }
}

public class WaveformRendererApngTests
{
    [Fact]
    public void ExportApng_CreatesNonEmptyFile()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.apng");
        try
        {
            StaHelper.Run(() =>
            {
                var peaks = Enumerable.Repeat(0.5f, 100).ToArray();
                WaveformRenderer.ExportApng(
                    outputPath, peaks, width: 400, height: 100,
                    barColor: System.Windows.Media.Colors.White, frameCount: 3, audioDurationMs: 3000);
            });
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}

public class WaveformRendererMovTests
{
    private static bool FfmpegAvailable
        => File.Exists(Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"));

    [Fact]
    public void ExportMov_CreatesNonEmptyFile()
    {
        if (!FfmpegAvailable) return;

        var outputPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.mov");
        try
        {
            StaHelper.Run(() =>
            {
                var peaks = Enumerable.Repeat(0.5f, 100).ToArray();
                WaveformRenderer.ExportMov(
                    outputPath, peaks, width: 400, height: 100,
                    barColor: System.Windows.Media.Colors.White, frameCount: 3, audioDurationSeconds: 3.0);
            });
            Assert.True(File.Exists(outputPath));
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [Fact]
    public void ExportMov_TempDirCleanedUpAfterExport()
    {
        if (!FfmpegAvailable) return;

        var outputPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.mov");
        string tempDir = "";
        try
        {
            StaHelper.Run(() =>
            {
                var peaks = Enumerable.Repeat(0.5f, 100).ToArray();
                tempDir = WaveformRenderer.ExportMov(
                    outputPath, peaks, 400, 100,
                    System.Windows.Media.Colors.White, 3, 3.0);
            });
            Assert.True(File.Exists(outputPath));
            Assert.False(Directory.Exists(tempDir), $"Temp dir was not deleted: {tempDir}");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
