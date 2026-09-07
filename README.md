# UnityMazer

A Unity 6 maze explorer with a live procedural orchestral score. Generate a maze and watch agents split at junctions, leave coloured trails and find the exit.

The music roadmap is implemented on `main`: four styles, ensemble layers driven by live paths, a deterministic presolve, and an ending forecast that brings the score home with the solver. [TODO.md](TODO.md) now tracks listening, mix, platform and release work.

## Current state

The project still reports version `0.1.0`, but `main` has moved on from the public `v0.1.0` tag. That release points to `a0c6f2b`: it includes the first live Cinematic orchestra and the platform build entry points, but it predates Ambient, EDM and Classical, the presolve and ending forecast, the collapsible solve panel, and hover help. Do not use the published `v0.1.0` zip to assess the current code.

The ignored local macOS build was rebuilt after the current feature work on 6 September 2026. It is a universal Apple Silicon and Intel app, targets macOS 12.0 or later, uses bundle ID `net.the-oneills.unitymazer`, and passes `codesign --verify --deep --strict` with an ad-hoc signature. The app has not yet completed the cross-machine macOS checklist below. Windows and Linux build methods exist, but this Unity installation has only `MacStandaloneSupport`; those builds remain blocked on their Unity Hub modules.

The editor validators cover solver and presolve trace equality, a 50 ms ceiling for a 100x100 presolve, ensemble hysteresis and density limits, forecast handling, all four composers, seeded output, recording, fixed audio-thread storage, clean endings and stops, and renderer timing. Manual listening and mix tuning remain. The repository contains 235 recorded orchestral samples from VSCO 2 Community Edition, seven generated EDM instruments and nine recorded soundtrack files.

## Run

Open the project with **Unity 6000.6.0f1**, load `Assets/Scenes/MazeSolver.unity`, and press Play. In a standalone build, click **Generate & Solve**. Space pauses and resumes.

The Orchestra mode composes notes at runtime from rules and solver activity. It uses recorded individual instruments from VSCO 2 Community Edition. It uses no trained model or external generation service. Soundtrack mode plays the existing recorded tracks.

Choose **Orchestra (live)** to hear the procedural score, then click **Generate & Solve**. **Soundtrack (recorded)** selects the pre-existing recordings.

**Style** selects the musical language for the next run: **Cinematic** (the original sound: repeating string patterns, orchestral layers, brass themes and percussion), **Ambient** (slow harmony, sustained textures, sparse motifs, long releases, no driving beat), **EDM** (kick and bass foundation, repeating hooks, hats and claps, filter movement, builds, breakdowns and drops, with the kick ducking bass and pad) and **Classical** (periodic phrasing, thematic development, counterpoint, prepared cadences, timpani at cadences only). Each style keeps seeded variation, key selection, live maze response and WAV saving; the style name appears in recording filenames. EDM's synth voices are generated at load, without new sample assets.

**The ensemble follows the maze.** The number of audible parts tracks the number of currently active paths, smoothed with entry and exit thresholds so parts do not flicker; parts enter at bar boundaries and leave at phrase boundaries with musical releases. Each style defines its own progression from a small core to the full arrangement, adding distinct roles before doubling. Density caps the ensemble size.

**The ending lands with the solve.** Before playback the maze is presolved with the same traversal rules as the visible solver, producing a solve sheet of per-step path counts and the exact terminal step. During playback the observed step rate turns remaining steps into a musical forecast: the score prepares its ending in the final approach (dominant preparation, thinning, a bounded tempo nudge) so the cadence resolves as the maze is solved. If the forecast is ever unreliable the score falls back to reacting at the terminal step, exactly as before.

Audio controls set music and accent volume, energy, density, variation, tempo and hall amount. Scroll the left panel when controls extend below the window.

**Key and seed** apply to the next run. Choose any of twelve tonics and Major or Minor; Bb is B flat. D minor is the default. **New seed each run: ON** generates a fresh music seed at Generate & Solve. Turn it off to edit or reuse the displayed seed. The music seed sets compositional choices; exact audio also depends on maze activity, settings and their timing.

**Variation** defaults to 35% and changes at the next eight-bar phrase. At zero the seeded theme and arrangement remain stable. Higher values develop the motif, shift repeating patterns and hand parts between instruments, with broader harmonic changes above 50%. The opening material returns every fourth phrase. Energy and Density remain separate controls.

**Save WAV** becomes available when a run and its musical ending finish. It saves the latest completed orchestral performance as stereo 24-bit PCM at the current output sample rate. Pauses, mix changes, accents and the reverb tail are included. Each save creates a new file under Unity's persistent data directory, in `Recordings`; **Show Recordings Folder** opens that location. On macOS this is normally `~/Library/Application Support/Matt O'Neill/UnityMazer/Recordings`. The status shows the exact saved path.

A new run retains the previous completed take until its replacement finishes. Reset, restart, switching to recorded soundtracks or changing audio devices discards an unfinished take. Save a take before quitting: temporary captures are removed on normal shutdown. Soundtrack recordings are excluded. Disk errors or recording-buffer overflow discard the affected capture and leave playback running.

Tempo changes take effect at the next bar. The solver speed does not set the musical tempo. A solved maze receives a short cadence while the result remains visible.

## Implementation map

- `Assets/Scripts/Maze` contains Growing Tree generation, the deterministic forking solver, the headless presolve and the solve sheet.
- `Assets/Scripts/Audio` contains the main-thread conductor and audio bridge. `Audio/Orchestra` contains the four composers, ensemble tracking, sample renderer, hall, limiter and background WAV recorder.
- `Assets/Scripts/Rendering` draws the maze and solve tree into `Texture2D` surfaces. `Assets/Scripts/UI` owns the runtime controls and shared hover help.
- `Assets/Editor` rebuilds the scene, prepares assets, runs validation, assigns the icon and builds players. `Assets/Scenes/MazeSolver.unity` is the only enabled scene.

## Build and validate

From the project directory:

```sh
UNITY='/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity'
"$UNITY" -batchmode -nographics -quit -projectPath "$PWD" \
  -executeMethod MazeSolver.Editor.BuildOrchestra.PrepareAndValidate \
  -logFile /tmp/unity-orchestra-validation.log
"$UNITY" -batchmode -nographics -quit -projectPath "$PWD" \
  -executeMethod MazeSolver.Editor.BuildOrchestra.BuildMac \
  -logFile /tmp/unity-orchestra-build.log
```

The first command creates the score and bank assets if missing, rebuilds the maze scene from its editor tool, runs the presolve, feature, style and renderer checks, and renders four 80-second comparison files under `Builds/Audio`: the default seed 431, seed 432, Bb minor at 80% variation, and C major at zero variation. It rewrites the saved scene, so preserve any manual scene edits first. These are editor checks, not Unity Test Framework tests. The second command writes `Builds/UnityMazer.app`.

### Memory audit

The project includes a Development-build memory runner and a macOS LaunchServices wrapper. It checks repeated maze replacement, recording cancellation, soundtrack cycling and audio renderer rebuilds. It records Unity memory counters, native object counts, RSS and `vmmap` summaries. See [MEMORY_AUDIT.md](MEMORY_AUDIT.md) for the current findings, baseline and pass limits.

```sh
"$UNITY" -batchmode -nographics -quit -projectPath "$PWD" \
  -executeMethod MazeSolver.Editor.BuildOrchestra.BuildMacMemoryAudit \
  -logFile /tmp/unity-memory-audit-build.log
scripts/run_memory_audit_macos.sh
```

The wrapper launches the `.app` through `open`. Do not start `Contents/MacOS/UnityMazer` directly when testing a windowed macOS player.

### Windows and Linux

`BuildOrchestra.BuildWindows` writes `Builds/Windows/UnityMazer.exe` and `BuildOrchestra.BuildLinux` writes `Builds/Linux/UnityMazer`. Substitute either method name in the build command above. Both need their Unity build-support module installed through Unity Hub: Windows Build Support (Mono) and Linux Build Support (Mono). The current Unity 6000.6.0f1 installation contains only macOS standalone support.

### App icon

`MazeSolver.Editor.SetAppIcon.Apply` assigns `Assets/Textures/AppIcon.png` as the Standalone icon for all platforms. Run it once after replacing the icon image, then rebuild.

### Distribution

`Builds/` is gitignored. The current public release is [UnityMazer 0.1.0](https://github.com/tmattoneill/unity-mazer/releases/tag/v0.1.0), with one 209.5 MiB macOS zip. It predates the current four-style and UI work. Publish a new version only after the listening and compatibility passes.

The macOS app is ad-hoc signed, without notarization, so Gatekeeper blocks a plain double-click on other machines. Right-click the app, choose Open, and confirm; or clear the quarantine flag with `xattr -dr com.apple.quarantine UnityMazer.app`.

### macOS compatibility checklist

Record the macOS version, CPU and audio device for each pass, then check:

- [ ] The app opens through Gatekeeper with the right-click Open route.
- [ ] Generate & Solve runs from 5x5 through 100x100, agents animate, and pause, resume and reset work.
- [ ] Cinematic, Ambient, EDM and Classical play without gaps, clipping or stuck voices.
- [ ] Live path growth and collapse audibly change the ensemble, and each ending resolves close to the visual solve.
- [ ] Soundtrack mode plays and cycles through the nine recorded tracks.
- [ ] Save WAV writes a stereo 24-bit file to `~/Library/Application Support/Matt O'Neill/UnityMazer/Recordings` and Show Recordings Folder opens it.
- [ ] The window resizes, the left panel scrolls, the solve panel hides and returns, and hover help remains on-screen.
- [ ] Quit is clean and Console records no UnityMazer crash.

Checks cover all 24 keys, distinct seeded compositions, phrase-boundary variation, returning themes, WAV sample accuracy, pause and tail capture, cancellation and writer errors, repeatable output from a seed, bounded accents, note ranges, callback allocations, pause/resume, cadence completion, stopped voices, finite output, peak level and audio processing time. They also verify the presolve trace matches the live solver step for step across sizes and path targets, the presolve time budget, ensemble layer hysteresis (no flicker, boundary-only changes, the Density ceiling, collapse recovery), the conductor's remaining-time forecast including pause and speed changes and divergence fallback, and each style's content rules (Ambient texture, EDM kick and breakdown pattern, Classical scoring, resolving endings) plus renderer determinism, allocation-free rendering and clean stops for all four styles. Comparison WAVs include seeds 431 and 432, Bb minor at 80% variation, and C major at zero variation. The demos follow a fixed activity trace through contrasting sections and an ending. It supports listening and tuning separately from maze generation.

## Score and instrument editing

`Assets/Resources/Orchestra/ScoreRules.asset` contains the four eight-bar scale-degree progression templates per mode, motif and section energy settings for the Cinematic style; `AmbientScoreRules.asset`, `EdmScoreRules.asset` and `ClassicalScoreRules.asset` carry the same shape for the other styles, and a missing style asset falls back to the Cinematic rules. The composer develops the motif across eight-bar phrases and four-section forms, assigning parts to strings, brass, woodwinds and percussion. It groups solver events into at most two musical accents per beat.

`InstrumentBank.asset` points to `manifest.json`. Each manifest entry supplies a resource, instrument, root MIDI note, velocity layer, alternate take and optional tuning correction. Source filenames use C3 for MIDI 60. Timpani tuning is corrected separately. Optional loop frame bounds must be validated against the PCM recording; the current bank uses natural recordings and rearticulation, without fabricated sustain loops.

Samples load before playback. At most four seconds of each recording are decoded, keeping the bank under 256 MiB of PCM. Short-note recordings remain intact; the sampler fades at natural ends. The renderer uses 96 fixed voices, sample-timed note starts, stereo panning, a damped shared hall and a stereo-linked peak limiter. Musical composition runs at beat boundaries on the audio thread using fixed storage, so main-thread stalls cannot disrupt the beat. The main thread sends bounded state messages and never edits active voices.

Regenerate the sample bank with `python3 scripts/prepare_orchestra.py`. This uses Python's standard library and curl, downloads from a pinned source revision and records source paths and SHA-256 hashes. It moves superseded generated samples into the ignored build cache. It does not require pip packages.

## Sample credits

VSCO 2 Community Edition by Versilian Studios and its contributors. Source: <https://github.com/sgossner/VSCO-2-CE>. Revision: `440300901dfe9275fd84e0b7763af1f8443ae62e`. Licence: CC0 1.0, included at `Assets/Resources/Orchestra/LICENSE.txt`. Original recordings are retained in the bank; tuning and note envelopes are applied at playback.

The pre-existing soundtrack recordings retain their original provenance. This change does not add them to the CC0 sample licence.
