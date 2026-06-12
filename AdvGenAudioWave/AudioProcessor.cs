using NAudio.Wave;

namespace AdvGenAudioWave;

public sealed class AudioProcessor
{
    private readonly float[] _samples;

    public double TotalDurationSeconds { get; }
    public long TotalDurationMs => (long)(TotalDurationSeconds * 1000);

    private AudioProcessor(float[] samples, double durationSeconds)
    {
        _samples = samples;
        TotalDurationSeconds = durationSeconds;
    }

    public static AudioProcessor Load(string filePath)
    {
        using var reader = new AudioFileReader(filePath);

        if (reader.TotalTime.TotalMilliseconds == 0)
            throw new InvalidOperationException("MP3 file has zero duration.");

        var sampleCount = (int)(reader.Length / (reader.WaveFormat.BitsPerSample / 8));
        var rawSamples = new float[sampleCount];
        var read = reader.ToSampleProvider().Read(rawSamples, 0, sampleCount);
        rawSamples = rawSamples[..read];

        var channels = reader.WaveFormat.Channels;
        float[] mono;
        if (channels == 1)
        {
            mono = rawSamples;
        }
        else
        {
            mono = new float[rawSamples.Length / channels];
            for (var i = 0; i < mono.Length; i++)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++)
                    sum += rawSamples[i * channels + c];
                mono[i] = sum / channels;
            }
        }

        return new AudioProcessor(mono, reader.TotalTime.TotalSeconds);
    }

    // Internal factory for tests — skips NAudio, injects synthetic samples
    internal static AudioProcessor FromSamples(float[] samples, double durationSeconds)
        => new(samples, durationSeconds);

    public float[] ExtractPeaks(int barCount)
    {
        if (barCount <= 0) throw new ArgumentOutOfRangeException(nameof(barCount));
        if (_samples.Length == 0) return new float[barCount];

        var peaks = new float[barCount];
        var chunkSize = Math.Max(1, _samples.Length / barCount);

        for (var i = 0; i < barCount; i++)
        {
            var start = i * chunkSize;
            var end = Math.Min(start + chunkSize, _samples.Length);
            var max = 0f;
            for (var j = start; j < end; j++)
                max = Math.Max(max, Math.Abs(_samples[j]));
            peaks[i] = max;
        }

        var globalMax = peaks.Max();
        if (globalMax > 0f)
            for (var i = 0; i < peaks.Length; i++)
                peaks[i] /= globalMax;

        return peaks;
    }

    public float[] ExtractEnvelope(int frameCount)
    {
        if (frameCount <= 0) throw new ArgumentOutOfRangeException(nameof(frameCount));

        var env = new float[frameCount];
        if (_samples.Length == 0) return env;

        var chunkSize = Math.Max(1, _samples.Length / frameCount);
        for (var f = 0; f < frameCount; f++)
        {
            var start = f * chunkSize;
            if (start >= _samples.Length) { env[f] = 0f; continue; }
            var end = Math.Min(start + chunkSize, _samples.Length);

            double sumSq = 0;
            for (var j = start; j < end; j++)
                sumSq += (double)_samples[j] * _samples[j];
            env[f] = (float)Math.Sqrt(sumSq / (end - start));
        }

        var max = env.Max();
        if (max > 0f)
            for (var i = 0; i < env.Length; i++)
                env[i] /= max;

        return env;
    }
}
