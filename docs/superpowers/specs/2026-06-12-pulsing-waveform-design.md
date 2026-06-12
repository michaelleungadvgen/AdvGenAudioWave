# Pulsing (Audio-Reactive) Waveform — Design Spec

**Date:** 2026-06-12
**Status:** Approved
**Builds on:** `2026-06-08-audiowaveform-design.md`

---

## Overview

Add an **audio-reactive "pulse" animation** to the existing waveform exporter. Today the
animation is a static waveform silhouette with a vertical cursor sweeping left-to-right. This
feature makes the entire waveform **breathe up and down in sync with the music's loudness
envelope** — the silhouette grows at loud moments and shrinks toward the center line during
quiet passages.

The pulse is a *uniform vertical scaling* of the whole waveform about its center line; the
spatial shape of the bars (which represent the whole song laid out left-to-right) does not
change frame to frame, only the overall amplitude. The user can choose cursor sweep, pulse, or
both. The mode applies to **both APNG and MOV export** so the two formats stay visually
consistent.

This is a small, additive change. No new dependencies.

---

## Architecture

Unchanged three-layer structure from the base design:

| Layer | File | Change |
|---|---|---|
| UI | `MainWindow.xaml` / `.xaml.cs` | Add an animation-mode `ComboBox`; thread the mode + envelope into both export paths and the preview |
| Audio | `AudioProcessor.cs` | Add `ExtractEnvelope(int frameCount)` |
| Render | `WaveformRenderer.cs` | `RenderFrame` gains `envelopeScale` + `drawCursor`; export methods gain envelope + mode |

---

## Animation Modes

A single enum drives behavior:

```csharp
public enum AnimationMode { CursorSweep, Pulse, CursorAndPulse }
```

| Mode | Per-frame vertical scale | Cursor drawn? |
|---|---|---|
| `CursorSweep` | always `1.0` | yes |
| `Pulse` | `envelope[frame]` | no |
| `CursorAndPulse` *(default)* | `envelope[frame]` | yes |

Default is `CursorAndPulse` — gives both playback position and the reactive effect.

---

## Data Flow

### Step A — Envelope Extraction (`AudioProcessor.ExtractEnvelope`)

New method, parallel to `ExtractPeaks` but producing **one value per frame** (temporal) rather
than one per bar (spatial). It is independent of `barCount`, so width/bar settings and the pulse
are orthogonal.

```csharp
public float[] ExtractEnvelope(int frameCount)
```

1. Guard: `frameCount <= 0` → `ArgumentOutOfRangeException` (mirrors `ExtractPeaks`).
2. If `_samples.Length == 0` → return `new float[frameCount]` (all zeros).
3. `chunkSize = Math.Max(1, _samples.Length / frameCount)`.
4. For each frame `f` in `[0, frameCount)`:
   - `start = f * chunkSize`, `end = Math.Min(start + chunkSize, _samples.Length)`.
   - If `start >= _samples.Length` (can happen for the trailing frames when
     `frameCount > _samples.Length`), the chunk is empty → RMS `= 0`.
   - Otherwise compute **RMS**: `sqrt( sum(sample[j]^2) / (end - start) )` over `j in [start,end)`.
     Accumulate the sum of squares as a `double` to avoid `float` precision loss over large
     chunks, then cast the final RMS back to `float`.
5. Normalize: divide every value by the global max RMS so values land in `[0.0 … 1.0]`. If the
   global max is `0` (silence), leave the array as zeros (no divide-by-zero) — bars collapse to
   the center line.

**Why RMS over peak:** RMS tracks perceived loudness and produces a smooth breathing motion;
peak amplitude spikes on single transients and looks jittery. The base waveform silhouette still
uses peak (via `ExtractPeaks`) — only the temporal pulse uses RMS.

**Edge cases:**
- `frameCount == 1` → single RMS value over the whole song, normalized to `1.0` (or `0.0` if
  silent). A one-frame export is effectively a still; scale `1.0` renders it at full height.
- Very short audio where `frameCount > _samples.Length` → trailing frames get empty chunks
  (RMS `0`); leading frames still produce values. No crash.

### Step B — Frame Rendering (`WaveformRenderer.RenderFrame`)

The pulse is implemented as a vertical `ScaleTransform` about the center line, so the existing
**cached base waveform bitmap is preserved and reused** across all frames (the performance
optimization from the base design stays intact). Only the cheap transform changes per frame.

New signature:

```csharp
public static BitmapSource RenderFrame(
    BitmapSource baseWaveform, int frameIndex, int frameCount,
    int imageWidth, int imageHeight,
    double envelopeScale, bool drawCursor)
```

1. `centerY = imageHeight / 2.0`.
2. Open `DrawingContext` on a `DrawingVisual`.
3. `ctx.PushTransform(new ScaleTransform(1, envelopeScale, 0, centerY))` — vertical-only scale
   pivoting on the center line. `freeze` the transform.
4. `ctx.DrawImage(baseWaveform, new Rect(0, 0, imageWidth, imageHeight))`.
5. `ctx.Pop()` — the cursor must NOT be scaled.
6. If `drawCursor`: draw the 2px red cursor at `ComputeCursorX(...)`, full height (unchanged).
7. Render onto a `Pbgra32` `RenderTargetBitmap`, freeze, return.

**Equivalence guarantee:** `envelopeScale = 1.0, drawCursor = true` reproduces the current output
exactly (a scale of 1.0 about center is a no-op transform).

**Tradeoff (chosen):** vertical downscaling interpolates the bar tops/bottoms slightly. This is
barely visible because bars are not scaled horizontally. The rejected alternative — re-rendering
every bar at a scaled height each frame — is crisper but discards the cached-bitmap optimization
and is materially slower at high frame counts (up to 9999 frames × thousands of bars). The
transform approach is the recommended and chosen design.

### Step C — Export (`ExportApng` / `ExportMov`)

Both export methods gain two parameters: the precomputed `float[] envelope` and the
`AnimationMode mode`. They no longer hardcode the cursor.

For each frame `i`:
- `scale = mode == AnimationMode.CursorSweep ? 1.0 : envelope[i]`
- `drawCursor = mode != AnimationMode.Pulse`
- Call the new `RenderFrame(... scale, drawCursor)`.

Everything else (APNG `MagickImageCollection` assembly, frame delay, looping; MOV temp-PNG
sequence, `fps`, ProRes 4444 `yuva444p10le` encode, `finally` cleanup) is **unchanged**.

The caller (`MainWindow`) computes `envelope = _audioProcessor.ExtractEnvelope(frameCount)` and
passes it plus the selected mode into the export call.

---

## UI Changes

Add one `ComboBox` ("Animation:") near the Frames input in `MainWindow.xaml`, bound to the three
`AnimationMode` values, default `Cursor + Pulse`.

```
┌─────────────────────────────────────────────┐
│  Frames: [60]   Frame delay: 50 ms          │
│  Animation: [ Cursor + Pulse  ▼ ]           │
└─────────────────────────────────────────────┘
```

- Selection is read at export time; changing it does **not** require re-decoding audio.
- **Preview** continues to show a representative still: frame 0 at `envelopeScale = 1.0`,
  `drawCursor = false`. Rendering the preview at the live envelope could show a tiny waveform
  during a quiet intro, which is misleading — full-scale is the clearest representative still.
- Mode is read-only metadata for the renderer; it does not affect frame count, delay, or fps.

---

## Error Handling

No new failure modes. Existing handling covers everything:

| Scenario | Response |
|---|---|
| `frameCount <= 0` passed to `ExtractEnvelope` | `ArgumentOutOfRangeException` (UI already clamps Frames to `[1, 9999]`) |
| Silent / zero-energy audio | Envelope is all zeros; bars render flat at the center line; no divide-by-zero |
| Very short audio (`frameCount > sample count`) | Trailing frames RMS `0`; no crash |
| All existing MP3/dimension/export errors | Unchanged from base design |

---

## Testing

New unit tests (xUnit, alongside existing `AudioProcessorTests` / `WaveformRendererTests`):

**`ExtractEnvelope`:**
- Returns array of length `frameCount`.
- All values in `[0.0, 1.0]`.
- Non-silent input → max value `== 1.0` (normalization).
- Silent input (all-zero samples) → all values `0.0`.
- `frameCount == 1` → single value, `1.0` for non-silent.
- `ArgumentOutOfRangeException` for `frameCount <= 0`.
- A louder chunk yields a strictly larger envelope value than a quieter chunk (monotonicity:
  build synthetic samples with a known loud half and quiet half).

**`RenderFrame` (pulse):**
- `envelopeScale = 1.0, drawCursor = true` produces the same non-transparent pixel extent as the
  pre-feature renderer (equivalence).
- `envelopeScale < 1.0` produces a strictly smaller non-transparent vertical extent than
  `1.0` (bars shrink toward center). Measure by scanning rows for any non-transparent pixel.
- `drawCursor = false` produces no full-height red column.

---

## Out of Scope

- FFT / frequency-band ("spectrum analyzer") bars
- Scrolling waveform window
- Gamma / power "punch" curve on the envelope (linear RMS for v1)
- Per-bar (non-uniform) reactivity
- Audio embedded in the MOV
- Any change to dependencies, frame-timing math, or the ProRes encode pipeline
