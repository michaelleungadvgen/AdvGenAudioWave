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

    // ExtractEnvelope: returns one value per frame
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(60)]
    public void ExtractEnvelope_ReturnsCorrectLength(int frameCount)
    {
        var proc = AudioProcessor.FromSamples(new float[frameCount * 10], durationSeconds: 1.0);
        var env = proc.ExtractEnvelope(frameCount);
        Assert.Equal(frameCount, env.Length);
    }

    // ExtractEnvelope: silence → all zeros (no divide-by-zero)
    [Fact]
    public void ExtractEnvelope_Silence_ReturnsAllZeros()
    {
        var proc = AudioProcessor.FromSamples(new float[1000], durationSeconds: 1.0);
        var env = proc.ExtractEnvelope(frameCount: 10);
        Assert.All(env, v => Assert.Equal(0f, v));
    }

    // ExtractEnvelope: non-silent input normalizes so the max value is exactly 1.0
    [Fact]
    public void ExtractEnvelope_NonSilent_MaxIsOne()
    {
        var samples = new float[1000];
        for (var i = 0; i < samples.Length; i++) samples[i] = 0.3f;
        var proc = AudioProcessor.FromSamples(samples, durationSeconds: 1.0);
        var env = proc.ExtractEnvelope(frameCount: 10);
        Assert.Equal(1.0f, env.Max(), precision: 5);
    }

    // ExtractEnvelope: all values land in [0, 1]
    [Fact]
    public void ExtractEnvelope_AllValuesInUnitRange()
    {
        var samples = new float[1000];
        for (var i = 0; i < samples.Length; i++) samples[i] = (i % 7) / 7f;
        var proc = AudioProcessor.FromSamples(samples, durationSeconds: 1.0);
        var env = proc.ExtractEnvelope(frameCount: 20);
        Assert.All(env, v => Assert.InRange(v, 0f, 1f));
    }

    // ExtractEnvelope: a louder chunk yields a strictly larger value than a quieter chunk
    [Fact]
    public void ExtractEnvelope_LouderChunkExceedsQuieter()
    {
        var samples = new float[1000];
        for (var i = 0; i < 500; i++) samples[i] = 0.2f;        // quiet first half
        for (var i = 500; i < 1000; i++) samples[i] = 0.8f;     // loud second half
        var proc = AudioProcessor.FromSamples(samples, durationSeconds: 1.0);
        var env = proc.ExtractEnvelope(frameCount: 2);
        Assert.True(env[1] > env[0], $"expected loud env[1] ({env[1]}) > quiet env[0] ({env[0]})");
        Assert.Equal(1.0f, env[1], precision: 5);
    }

    // ExtractEnvelope: single frame over non-silent audio normalizes to 1.0
    [Fact]
    public void ExtractEnvelope_SingleFrame_NonSilent_IsOne()
    {
        var samples = new float[1000];
        for (var i = 0; i < samples.Length; i++) samples[i] = 0.5f;
        var proc = AudioProcessor.FromSamples(samples, durationSeconds: 1.0);
        var env = proc.ExtractEnvelope(frameCount: 1);
        Assert.Single(env);
        Assert.Equal(1.0f, env[0], precision: 5);
    }

    // ExtractEnvelope: invalid frame count throws (UI clamps to [1,9999], but guard anyway)
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ExtractEnvelope_InvalidFrameCount_Throws(int frameCount)
    {
        var proc = AudioProcessor.FromSamples(new float[100], durationSeconds: 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(() => proc.ExtractEnvelope(frameCount));
    }
}
