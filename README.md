# AdvGen Audio Wave

A Windows desktop app that turns an MP3 into an **animated waveform** with a transparent
background, exported as an **Animated PNG (APNG)** or a **ProRes 4444 MOV** with alpha — ready
to overlay on top of your audio in any video editor.

The waveform is drawn as mirrored bars (symmetric above and below the center line). You choose
how it animates: a cursor sweeping across it, the whole waveform pulsing in time with the
music's loudness, or both.

---

## Demo

[**▶ Watch the demo on YouTube**](https://youtu.be/Cr9mfQqWWaM) — an exported MOV (transparent
ProRes 4444) overlaid on video to produce the animated waveform effect.

---

## Features

- **MP3 input** — decoded to mono and analysed for peak amplitudes via [NAudio](https://github.com/naudio/NAudio).
- **Three animation modes:**
  - **Cursor sweep** — a vertical line sweeps left-to-right across a static waveform over the audio's duration.
  - **Pulse** — the entire waveform breathes up and down in sync with the music's loudness envelope (per-frame RMS).
  - **Cursor + Pulse** *(default)* — both at once.
- **Two export formats, both with a transparent background:**
  - **APNG** — looping animated PNG, assembled with [Magick.NET](https://github.com/dlemstra/Magick.NET).
  - **MOV** — ProRes 4444 with a real alpha channel (`yuva444p10le`), encoded via [FFMpegCore](https://github.com/rosenbjerg/FFMpegCore) + `ffmpeg`. **MOV length always matches the source MP3** — set the frame rate and the app renders exactly the right number of frames.
- **Live preview** — a representative still of the waveform updates as you change size and color.
- **Configurable** — output width/height, bar color, and frame count (which controls animation smoothness and loop timing).

---

## Requirements

- **Windows** (WPF app, targets `net8.0-windows`)
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** (to build) or the .NET 8 Desktop Runtime (to run a published build)
- **`ffmpeg.exe`** — required **only** for MOV export (see [ffmpeg setup](#ffmpeg-setup-for-mov-export) below). APNG export works without it.

---

## Build & Run

From the repository root:

```powershell
# Build everything
dotnet build AdvGenAudioWave.sln

# Run the app
dotnet run --project AdvGenAudioWave/AdvGenAudioWave.csproj
```

---

## Usage

1. **Browse MP3…** and select an audio file.
2. Set the **Width**, **Height**, and **Color** for the waveform. The preview updates live.
3. Set the animation rate:
   - **Frames** — used for **APNG**. More frames = smoother animation; the **Frame delay**
     label shows the resulting per-frame timing so the loop stays in sync with the audio.
   - **MOV FPS** — used for **MOV** (default 30, range 1–60). The MOV length always equals the
     MP3 length; the app renders `FPS × audio-duration` frames automatically. Long tracks at a
     high FPS mean many frames — slower export and a larger temporary folder — so lower the FPS
     for very long audio.
4. Pick an **Animation** mode: *Cursor sweep*, *Pulse*, or *Cursor + Pulse*.
5. **Export as APNG** or **Export as MOV** and choose where to save.
6. Overlay the result on your MP3 in a video editor — the transparent background composites
   cleanly over any footage.

> The export buttons stay disabled until a valid MP3 is loaded. **Export as MOV** additionally
> requires `ffmpeg.exe` to be present (see below).

---

## ffmpeg setup (for MOV export)

MOV export shells out to `ffmpeg` to encode ProRes 4444. The app looks for **`ffmpeg.exe` in its
own application directory** (it sets FFMpegCore's `BinaryFolder` to `AppContext.BaseDirectory`).
If it isn't found, the **Export as MOV** button is disabled with an explanatory tooltip; APNG
export is unaffected.

To enable MOV export, place an `ffmpeg.exe` next to the app's executable:

- For a development build, that's the build output directory, e.g.
  `AdvGenAudioWave\bin\Debug\net8.0-windows\ffmpeg.exe`.
- For a published build, place it alongside `AdvGenAudioWave.exe`.

You can download a static Windows build of ffmpeg from
[gyan.dev](https://www.gyan.dev/ffmpeg/builds/) or
[BtbN](https://github.com/BtbN/FFmpeg-Builds/releases), or install it with
`winget install Gyan.FFmpeg` and copy the resulting `ffmpeg.exe` into the app directory.

> A copy placed in the `bin\Debug\...` output directory is removed by a clean rebuild
> (`dotnet clean` or deleting `bin`); an incremental `dotnet build` leaves it in place.

---

## How it works

The app is a single WPF window (plain code-behind, no MVVM) over three focused pieces:

| Layer | File | Responsibility |
|---|---|---|
| UI | `MainWindow.xaml(.cs)` | File picker, settings, live preview, export buttons |
| Audio | `AudioProcessor.cs` | Decode MP3 → mono samples; extract per-bar **peaks** and per-frame **RMS envelope** |
| Render | `WaveformRenderer.cs` | Draw the mirrored-bar waveform and compose animation frames; assemble APNG / encode MOV |

- The static waveform bitmap is rendered **once** and reused for every frame.
- The **pulse** is a vertical scale of that cached bitmap about the center line (cheap per frame);
  the **cursor** is drawn on top, unaffected by the scale, so it stays full-height.
- Bar count is derived from the output width (`width / 4`, for 3px bars + 1px gaps).

---

## Testing

```powershell
dotnet test AdvGenAudioWave.sln
```

The xUnit suite covers the audio analysis (peak/RMS extraction, normalization, edge cases) and
the renderer (frame composition, pulse scaling, cursor placement, APNG/MOV export). WPF render
tests run on an STA thread. MOV export tests are skipped automatically when `ffmpeg.exe` is not
present.

---

## License

[MIT](LICENSE) © 2026 AdvGen.

Bundled/third-party components and their licenses are listed in
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). Note that `ffmpeg` is a separately-distributed
executable (not bundled in this repository) and carries its own license.
