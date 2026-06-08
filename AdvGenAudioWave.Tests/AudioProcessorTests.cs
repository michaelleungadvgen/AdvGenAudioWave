using AdvGenAudioWave;
using Xunit;

namespace AdvGenAudioWave.Tests;

public class AudioProcessorTests
{
    // ExtractPeaks: silence → all zeros
    [Fact]
    public void ExtractPeaks_Silence_ReturnsAllZeros()
    {
        var proc = AudioProcessor.FromSamples(new float[1000], durationSeconds: 1.0);
        var peaks = proc.ExtractPeaks(barCount: 10);
        Assert.Equal(10, peaks.Length);
        Assert.All(peaks, p => Assert.Equal(0f, p));
    }

    // ExtractPeaks: single full-amplitude spike normalizes to 1.0
    [Fact]
    public void ExtractPeaks_SingleSpike_NormalizesToOne()
    {
        var samples = new float[400];
        samples[0] = 1.0f; // spike in first bar
        var proc = AudioProcessor.FromSamples(samples, durationSeconds: 1.0);
        var peaks = proc.ExtractPeaks(barCount: 4);
        Assert.Equal(1.0f, peaks[0], precision: 5);
        Assert.Equal(0.0f, peaks[1], precision: 5);
    }

    // ExtractPeaks: negative samples use absolute value
    [Fact]
    public void ExtractPeaks_NegativeSamples_UsesAbsoluteValue()
    {
        var samples = new float[400];
        samples[200] = -0.5f;
        var proc = AudioProcessor.FromSamples(samples, durationSeconds: 1.0);
        var peaks = proc.ExtractPeaks(barCount: 4);
        Assert.Equal(1.0f, peaks[2], precision: 5); // normalized: -0.5 is global max → 1.0
    }

    // ExtractPeaks: barCount matches returned array length
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(480)]
    public void ExtractPeaks_ReturnsCorrectBarCount(int barCount)
    {
        var samples = new float[barCount * 10];
        var proc = AudioProcessor.FromSamples(samples, durationSeconds: 1.0);
        var peaks = proc.ExtractPeaks(barCount);
        Assert.Equal(barCount, peaks.Length);
    }

    // TotalDurationMs rounds correctly
    [Fact]
    public void TotalDurationMs_IsCorrect()
    {
        var proc = AudioProcessor.FromSamples(new float[100], durationSeconds: 3.5);
        Assert.Equal(3500L, proc.TotalDurationMs);
    }

    // Load invalid path throws
    [Fact]
    public void Load_InvalidPath_Throws()
    {
        Assert.ThrowsAny<Exception>(() => AudioProcessor.Load("nonexistent.mp3"));
    }
}
