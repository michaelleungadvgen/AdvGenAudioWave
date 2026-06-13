namespace AdvGenAudioWave;

/// <summary>
/// Progress update from a long-running export. <see cref="Fraction"/> is in [0, 1] during
/// frame rendering, or <c>double.NaN</c> for an indeterminate phase (e.g. the ffmpeg encode).
/// </summary>
public readonly record struct ExportProgress(string Phase, double Fraction);
