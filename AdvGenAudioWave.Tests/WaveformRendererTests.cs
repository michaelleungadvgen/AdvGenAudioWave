using Xunit;
using AdvGenAudioWave;

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
