# Pulsing (Audio-Reactive) Waveform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an audio-reactive "pulse" animation so the waveform breathes vertically in sync with the music's loudness envelope, selectable alongside the existing cursor sweep, for both APNG and MOV export.

**Architecture:** A new `AudioProcessor.ExtractEnvelope(frameCount)` produces one normalized RMS value per frame. `WaveformRenderer.RenderFrame` gains two optional parameters (`envelopeScale`, `drawCursor`) and applies a center-pivot vertical `ScaleTransform` to the cached base bitmap — reusing the existing once-per-export render. A new `AnimationMode` enum (CursorSweep / Pulse / CursorAndPulse) flows from a UI `ComboBox` through the export methods.

**Tech Stack:** C# / .NET 8 / WPF, xUnit tests (STA via `StaHelper`), NAudio, Magick.NET, FFMpegCore. Pixel format `Pbgra32` (byte order B,G,R,A).

**Spec:** `docs/superpowers/specs/2026-06-12-pulsing-waveform-design.md`

---

## Conventions for every task

- Run all commands from the repo root: `e:\Projects\AdvGenAudioWave`.
- Build: `dotnet build AdvGenAudioWave.sln`
- Run all tests: `dotnet test AdvGenAudioWave.sln`
- Run one test class/method: `dotnet test AdvGenAudioWave.sln --filter "FullyQualifiedName~<name>"`
- Renderer tests MUST run their WPF code inside `StaHelper.Run(() => { ... })` (WPF requires an STA thread). Math-only and `AudioProcessor` tests do not.
- The MOV export tests early-return when `ffmpeg.exe` is absent — do not treat a skipped MOV test as a failure.
- Commit after each task with the exact message shown. Stay on the `master` branch (this repo's working branch).

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `AdvGenAudioWave/AnimationMode.cs` | **Create** | Shared `AnimationMode` enum (referenced by renderer + UI) |
| `AdvGenAudioWave/AudioProcessor.cs` | Modify | Add `ExtractEnvelope(int frameCount)` |
| `AdvGenAudioWave/WaveformRenderer.cs` | Modify | `RenderFrame` gains `envelopeScale`/`drawCursor`; `ExportApng`/`ExportMov` gain `envelope`/`mode` |
| `AdvGenAudioWave/MainWindow.xaml` | Modify | Add Animation `ComboBox` |
| `AdvGenAudioWave/MainWindow.xaml.cs` | Modify | Read mode, compute envelope, pass into exports; preview drops cursor |
| `AdvGenAudioWave.Tests/AudioProcessorTests.cs` | Modify | `ExtractEnvelope` tests |
| `AdvGenAudioWave.Tests/WaveformRendererTests.cs` | Modify | Pulse render tests; update export test calls |

---

## Task 1: `AudioProcessor.ExtractEnvelope`

One normalized RMS value per frame. Additive — no existing code changes, build stays green.

**Files:**
- Modify: `AdvGenAudioWave/AudioProcessor.cs`
- Test: `AdvGenAudioWave.Tests/AudioProcessorTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `AudioProcessorTests.cs` inside the existing `AudioProcessorTests` class (after the `ExtractPeaks` tests):

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test AdvGenAudioWave.sln --filter "FullyQualifiedName~ExtractEnvelope"`
Expected: compile error / FAIL — `ExtractEnvelope` does not exist.

- [ ] **Step 3: Implement `ExtractEnvelope`**

Add this method to `AudioProcessor.cs` immediately after `ExtractPeaks`:

```csharp
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
```

(`System.Linq` is already available — `ExtractPeaks` uses `.Max()`.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test AdvGenAudioWave.sln --filter "FullyQualifiedName~ExtractEnvelope"`
Expected: PASS (all 9 cases).

- [ ] **Step 5: Commit**

```bash
git add AdvGenAudioWave/AudioProcessor.cs AdvGenAudioWave.Tests/AudioProcessorTests.cs
git commit -m "feat: AudioProcessor.ExtractEnvelope — per-frame normalized RMS"
```

---

## Task 2: `AnimationMode` enum + `RenderFrame` pulse support

`RenderFrame` gets two optional parameters with defaults that exactly reproduce today's output (`envelopeScale = 1.0`, `drawCursor = true`), so existing callers and tests compile and pass unchanged. The pulse is a center-pivot vertical `ScaleTransform` around the cached bitmap.

**Files:**
- Create: `AdvGenAudioWave/AnimationMode.cs`
- Modify: `AdvGenAudioWave/WaveformRenderer.cs:55-69` (the `RenderFrame` method)
- Test: `AdvGenAudioWave.Tests/WaveformRendererTests.cs`

- [ ] **Step 1: Create the `AnimationMode` enum**

Create `AdvGenAudioWave/AnimationMode.cs`:

```csharp
namespace AdvGenAudioWave;

public enum AnimationMode
{
    CursorSweep,
    Pulse,
    CursorAndPulse
}
```

- [ ] **Step 2: Write the failing tests**

Add to `WaveformRendererTests.cs` inside the existing `WaveformRendererRenderTests` class. The first helper measures how many rows contain any non-transparent pixel (the waveform's vertical extent):

```csharp
// Counts image rows containing at least one non-transparent pixel.
private static int OpaqueRowCount(System.Windows.Media.Imaging.BitmapSource bmp)
{
    var w = bmp.PixelWidth;
    var h = bmp.PixelHeight;
    var pixels = new byte[w * h * 4];
    bmp.CopyPixels(pixels, w * 4, 0);
    var rows = 0;
    for (var y = 0; y < h; y++)
    {
        var rowStart = y * w * 4;
        for (var x = 0; x < w; x++)
        {
            if (pixels[rowStart + x * 4 + 3] != 0) { rows++; break; }
        }
    }
    return rows;
}

[Fact]
public void RenderFrame_PulseScaleBelowOne_ShrinksVerticalExtent()
{
    StaHelper.Run(() =>
    {
        var baseWaveform = WaveformRenderer.RenderBaseWaveform(
            FlatPeaks(10, 1.0f), 40, 100, Colors.White);   // full-height bars

        var full = WaveformRenderer.RenderFrame(
            baseWaveform, 0, 10, 40, 100, envelopeScale: 1.0, drawCursor: false);
        var half = WaveformRenderer.RenderFrame(
            baseWaveform, 0, 10, 40, 100, envelopeScale: 0.5, drawCursor: false);

        Assert.True(OpaqueRowCount(half) < OpaqueRowCount(full),
            $"half extent ({OpaqueRowCount(half)}) should be < full ({OpaqueRowCount(full)})");
    });
}

[Fact]
public void RenderFrame_DrawCursorFalse_ProducesNoOpaquePixelsOnSilentWaveform()
{
    StaHelper.Run(() =>
    {
        var baseWaveform = WaveformRenderer.RenderBaseWaveform(
            FlatPeaks(10, 0f), 40, 20, Colors.White);      // no bars at all
        var frame = WaveformRenderer.RenderFrame(
            baseWaveform, 0, 10, 40, 20, envelopeScale: 1.0, drawCursor: false);

        var pixels = new byte[40 * 20 * 4];
        frame.CopyPixels(pixels, 40 * 4, 0);
        for (var i = 3; i < pixels.Length; i += 4)
            Assert.Equal(0, pixels[i]);   // fully transparent — no cursor column
    });
}
```

Note: the existing `RenderFrame_CursorAtExpectedPosition` test (calling the 5-argument form) is the equivalence check — it must still pass unchanged after this task.

- [ ] **Step 3: Run the new tests to verify they fail**

Run: `dotnet test AdvGenAudioWave.sln --filter "FullyQualifiedName~RenderFrame_Pulse|FullyQualifiedName~RenderFrame_DrawCursor"`
Expected: compile error / FAIL — `RenderFrame` has no `envelopeScale`/`drawCursor` parameters.

- [ ] **Step 4: Modify `RenderFrame`**

Replace the entire `RenderFrame` method in `WaveformRenderer.cs` with:

```csharp
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
```

- [ ] **Step 5: Run the renderer tests to verify they pass**

Run: `dotnet test AdvGenAudioWave.sln --filter "FullyQualifiedName~WaveformRendererRenderTests"`
Expected: PASS — including the unchanged `RenderFrame_CursorAtExpectedPosition` equivalence test.

- [ ] **Step 6: Commit**

```bash
git add AdvGenAudioWave/AnimationMode.cs AdvGenAudioWave/WaveformRenderer.cs AdvGenAudioWave.Tests/WaveformRendererTests.cs
git commit -m "feat: RenderFrame pulse via vertical scale + AnimationMode enum"
```

---

## Task 3: Export methods take envelope + mode

`ExportApng` and `ExportMov` gain `float[] envelope` and `AnimationMode mode` parameters and compute per-frame scale/cursor from them. Existing export tests are updated to the new signatures in the same task to keep the build green.

**Files:**
- Modify: `AdvGenAudioWave/WaveformRenderer.cs:71-138` (`ExportApng`, `ExportMov`)
- Test: `AdvGenAudioWave.Tests/WaveformRendererTests.cs`

- [ ] **Step 1: Update the export tests to the new signatures (and add a Pulse-mode case)**

In `WaveformRendererApngTests.ExportApng_CreatesNonEmptyFile`, change the `ExportApng` call to pass an envelope and mode:

```csharp
var peaks = Enumerable.Repeat(0.5f, 100).ToArray();
var envelope = Enumerable.Repeat(1.0f, 3).ToArray();   // frameCount = 3
WaveformRenderer.ExportApng(
    outputPath, peaks, width: 400, height: 100,
    barColor: System.Windows.Media.Colors.White, frameCount: 3, audioDurationMs: 3000,
    envelope: envelope, mode: AnimationMode.CursorAndPulse);
```

In both `WaveformRendererMovTests` tests, change the `ExportMov` calls the same way:

```csharp
var peaks = Enumerable.Repeat(0.5f, 100).ToArray();
var envelope = Enumerable.Repeat(1.0f, 3).ToArray();
// CreatesNonEmptyFile:
WaveformRenderer.ExportMov(
    outputPath, peaks, width: 400, height: 100,
    barColor: System.Windows.Media.Colors.White, frameCount: 3, audioDurationSeconds: 3.0,
    envelope: envelope, mode: AnimationMode.CursorAndPulse);
// TempDirCleanedUpAfterExport:
tempDir = WaveformRenderer.ExportMov(
    outputPath, peaks, 400, 100,
    System.Windows.Media.Colors.White, 3, 3.0,
    envelope, AnimationMode.CursorAndPulse);
```

Add one new APNG test (Pulse mode still produces a file) to `WaveformRendererApngTests`:

```csharp
[Fact]
public void ExportApng_PulseMode_CreatesNonEmptyFile()
{
    var outputPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.apng");
    try
    {
        StaHelper.Run(() =>
        {
            var peaks = Enumerable.Repeat(0.5f, 100).ToArray();
            var envelope = new[] { 0.2f, 0.6f, 1.0f };
            WaveformRenderer.ExportApng(
                outputPath, peaks, 400, 100,
                System.Windows.Media.Colors.White, 3, 3000,
                envelope, AnimationMode.Pulse);
        });
        Assert.True(new FileInfo(outputPath).Length > 0);
    }
    finally
    {
        if (File.Exists(outputPath)) File.Delete(outputPath);
    }
}
```

- [ ] **Step 2: Run the export tests to verify they fail**

Run: `dotnet test AdvGenAudioWave.sln --filter "FullyQualifiedName~Apng|FullyQualifiedName~Mov"`
Expected: compile error / FAIL — `ExportApng`/`ExportMov` do not yet accept `envelope`/`mode`.

- [ ] **Step 3: Update `ExportApng`**

Change the `ExportApng` signature and its render call in `WaveformRenderer.cs`:

```csharp
public static void ExportApng(
    string outputPath, float[] peaks, int width, int height,
    System.Windows.Media.Color barColor, int frameCount, long audioDurationMs,
    float[] envelope, AnimationMode mode)
{
    var baseWaveform = RenderBaseWaveform(peaks, width, height, barColor);
    var frameDelayCs = ComputeFrameDelayCs(audioDurationMs, frameCount);

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
    }

    if (collection.Count > 0)
        collection[0].AnimationIterations = 0; // loop forever

    collection.Write(outputPath, MagickFormat.APng);
}
```

- [ ] **Step 4: Update `ExportMov`**

Change the `ExportMov` signature and its render call:

```csharp
public static string ExportMov(
    string outputPath, float[] peaks, int width, int height,
    System.Windows.Media.Color barColor, int frameCount, double audioDurationSeconds,
    float[] envelope, AnimationMode mode)
{
    var fps = ComputeFps(frameCount, audioDurationSeconds);
    var baseWaveform = RenderBaseWaveform(peaks, width, height, barColor);
    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);

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
        }

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
```

- [ ] **Step 5: Run the export tests to verify they pass**

Run: `dotnet test AdvGenAudioWave.sln --filter "FullyQualifiedName~Apng|FullyQualifiedName~Mov"`
Expected: PASS (MOV tests pass, or no-op early-return if `ffmpeg.exe` is absent).

- [ ] **Step 6: Commit**

```bash
git add AdvGenAudioWave/WaveformRenderer.cs AdvGenAudioWave.Tests/WaveformRendererTests.cs
git commit -m "feat: ExportApng/ExportMov accept envelope + AnimationMode"
```

---

## Task 4: UI — animation mode selector + wiring

Add the `ComboBox`, read the selected mode at export time, compute the envelope, and pass both into the export calls. Preview drops the cursor (intentional, per spec). This is UI glue — verified by build + manual run, not unit tests.

**Files:**
- Modify: `AdvGenAudioWave/MainWindow.xaml:40-44` (Settings row)
- Modify: `AdvGenAudioWave/MainWindow.xaml.cs` (`RefreshPreview`, `ExportApng_Click`, `ExportMov_Click`; add `GetAnimationMode`)

- [ ] **Step 1: Add the ComboBox to the XAML**

In `MainWindow.xaml`, inside the Row 1 `WrapPanel`, insert these two elements immediately after the `FrameDelayLabel` `TextBlock` (the last child before `</WrapPanel>`):

```xml
            <Label Content="Animation:" VerticalAlignment="Center" Margin="12,0,0,0"/>
            <ComboBox x:Name="AnimationModeBox" Width="120" SelectedIndex="2"
                      VerticalContentAlignment="Center" Margin="0,0,0,0">
                <ComboBoxItem Content="Cursor sweep"/>
                <ComboBoxItem Content="Pulse"/>
                <ComboBoxItem Content="Cursor + Pulse"/>
            </ComboBox>
```

`SelectedIndex="2"` makes **Cursor + Pulse** the default.

- [ ] **Step 2: Add the `GetAnimationMode` helper**

In `MainWindow.xaml.cs`, add this method (e.g. just after `TryParseInput`):

```csharp
private AnimationMode GetAnimationMode() => AnimationModeBox.SelectedIndex switch
{
    0 => AnimationMode.CursorSweep,
    1 => AnimationMode.Pulse,
    _ => AnimationMode.CursorAndPulse,
};
```

- [ ] **Step 3: Update `RefreshPreview` to drop the cursor**

In `MainWindow.xaml.cs`, change the preview frame call inside `RefreshPreview`:

```csharp
var frame0 = WaveformRenderer.RenderFrame(
    baseWaveform, 0, 1, width, height, envelopeScale: 1.0, drawCursor: false);
```

- [ ] **Step 4: Pass envelope + mode into both export handlers**

Both handlers already declare `var barCount = ...` and `var peaks = ...`. Do **not** re-declare them — only add the `envelope` line and replace the export call.

In `ExportApng_Click`, the existing three lines are:

```csharp
var barCount = WaveformRenderer.ComputeBarCount(width);
var peaks = _audioProcessor.ExtractPeaks(barCount);
WaveformRenderer.ExportApng(
    dialog.FileName, peaks, width, height, _waveformColor,
    frameCount, _audioProcessor.TotalDurationMs);
```

Insert the `envelope` line after `peaks` and replace the `ExportApng(...)` call so the block reads:

```csharp
var barCount = WaveformRenderer.ComputeBarCount(width);
var peaks = _audioProcessor.ExtractPeaks(barCount);
var envelope = _audioProcessor.ExtractEnvelope(frameCount);
WaveformRenderer.ExportApng(
    dialog.FileName, peaks, width, height, _waveformColor,
    frameCount, _audioProcessor.TotalDurationMs,
    envelope, GetAnimationMode());
```

In `ExportMov_Click`, make the same edit — add the `envelope` line and replace the `ExportMov(...)` call (note `TotalDurationSeconds`, not `TotalDurationMs`):

```csharp
var barCount = WaveformRenderer.ComputeBarCount(width);
var peaks = _audioProcessor.ExtractPeaks(barCount);
var envelope = _audioProcessor.ExtractEnvelope(frameCount);
WaveformRenderer.ExportMov(
    dialog.FileName, peaks, width, height, _waveformColor,
    frameCount, _audioProcessor.TotalDurationSeconds,
    envelope, GetAnimationMode());
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build AdvGenAudioWave.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Manual verification (REQUIRED SUB-SKILL: superpowers:verification-before-completion)**

Run the app: `dotnet run --project AdvGenAudioWave\AdvGenAudioWave.csproj`
Then confirm:
1. The "Animation:" dropdown appears, defaulting to **Cursor + Pulse**.
2. Load an MP3; the preview shows the waveform with **no red cursor column**.
3. Export as APNG with **Pulse** selected → open the `.apng` (e.g. in a browser): the waveform breathes up/down with the music, no cursor.
4. Export as APNG with **Cursor sweep** selected → the cursor sweeps over a static (non-pulsing) waveform, matching the original behavior.
5. If `ffmpeg.exe` is present, repeat one MOV export and confirm the `.mov` plays with the chosen animation.

- [ ] **Step 7: Commit**

```bash
git add AdvGenAudioWave/MainWindow.xaml AdvGenAudioWave/MainWindow.xaml.cs
git commit -m "feat: animation mode selector wired into preview and export"
```

---

## Task 5: Full regression + finish

- [ ] **Step 1: Run the entire suite**

Run: `dotnet test AdvGenAudioWave.sln`
Expected: PASS — all existing tests plus the new `ExtractEnvelope`, pulse-render, and pulse-export tests. (MOV tests no-op if `ffmpeg.exe` is absent.)

- [ ] **Step 2: Finish the branch**

REQUIRED SUB-SKILL: Use superpowers:finishing-a-development-branch to decide how to integrate the work (merge / PR / cleanup).
