# UnityMazer

A Unity 6 maze explorer with a live procedural orchestral score. Generate a maze and watch agents split at junctions, leave coloured trails and find the exit.

Planned music work is tracked in [TODO.md](TODO.md): styles, ensemble size driven by live paths, and presolving to pace the composition and its ending.

## Run

Open the project with **Unity 6000.6.0f1**, load `Assets/Scenes/MazeSolver.unity`, and press Play. In a standalone build, click **Generate & Solve**. Space pauses and resumes.

The Orchestra mode composes notes at runtime from rules and solver activity. It uses recorded individual instruments from VSCO 2 Community Edition. It uses no trained model or external generation service. Soundtrack mode plays the existing recorded tracks.

Choose **Orchestra (live)** to hear the procedural score, then click **Generate & Solve**. **Soundtrack (recorded)** selects the pre-existing recordings.

Audio controls set music and accent volume, energy, density, variation, tempo and hall amount. Scroll the left panel when controls extend below the window.

**Key and seed** apply to the next run. Choose any of twelve tonics and Major or Minor; Bb is B flat. D minor is the default. **New seed each run: ON** generates a fresh music seed at Generate & Solve. Turn it off to edit or reuse the displayed seed. The music seed sets compositional choices; exact audio also depends on maze activity, settings and their timing.

**Variation** defaults to 35% and changes at the next eight-bar phrase. At zero the seeded theme and arrangement remain stable. Higher values develop the motif, shift repeating patterns and hand parts between instruments, with broader harmonic changes above 50%. The opening material returns every fourth phrase. Energy and Density remain separate controls.

**Save WAV** becomes available when a run and its musical ending finish. It saves the latest completed orchestral performance as stereo 24-bit PCM at the current output sample rate. Pauses, mix changes, accents and the reverb tail are included. Each save creates a new file under Unity's persistent data directory, in `Recordings`; **Show Recordings Folder** opens that location. On macOS this is normally `~/Library/Application Support/Matt O'Neill/UnityMazer/Recordings`. The status shows the exact saved path.

A new run retains the previous completed take until its replacement finishes. Reset, restart, switching to recorded soundtracks or changing audio devices discards an unfinished take. Save a take before quitting: temporary captures are removed on normal shutdown. Soundtrack recordings are excluded. Disk errors or recording-buffer overflow discard the affected capture and leave playback running.

 Tempo changes take effect at the next bar. The solver speed does not set the musical tempo. A solved maze receives a short cadence while the result remains visible.

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

The first command creates the score and bank assets if missing, rebuilds the maze scene from its editor tool, runs audio checks, and renders `Builds/Audio/Orchestra-demo.wav`. It rewrites the saved scene, so preserve any manual scene edits first. The second writes `Builds/UnityMazer.app`.

### Windows and Linux

`BuildOrchestra.BuildWindows` writes `Builds/Windows/UnityMazer.exe` and `BuildOrchestra.BuildLinux` writes `Builds/Linux/UnityMazer`. Substitute either method name in the build command above. Both need their Unity build-support module installed through Unity Hub (Windows Build Support (Mono) and Linux Build Support (Mono)); the editor's default install includes only macOS and WebGL.

### App icon

`MazeSolver.Editor.SetAppIcon.Apply` assigns `Assets/Textures/AppIcon.png` as the Standalone icon for all platforms. Run it once after replacing the icon image, then rebuild.

### Distribution

`Builds/` is gitignored. Finished builds are attached as zip assets to GitHub Releases, tagged by version. The macOS app is ad-hoc signed, without notarization, so Gatekeeper blocks a plain double-click on other machines. Right-click the app, choose Open, and confirm; or clear the quarantine flag with `xattr -dr com.apple.quarantine UnityMazer.app`.

### macOS compatibility checklist

When testing on another macOS version or machine: the app launches past Gatekeeper with the right-click Open route; Generate & Solve runs and agents animate; Orchestra mode plays without glitches; Save WAV writes to `~/Library/Application Support/Matt O'Neill/UnityMazer/Recordings`; the window resizes and the left panel scrolls; quit is clean with no crash log in Console.

Checks cover all 24 keys, distinct seeded compositions, phrase-boundary variation, returning themes, WAV sample accuracy, pause and tail capture, cancellation and writer errors, repeatable output from a seed, bounded accents, note ranges, callback allocations, pause/resume, cadence completion, stopped voices, finite output, peak level and audio processing time. Comparison WAVs include seeds 431 and 432, Bb minor at 80% variation, and C major at zero variation. The demos follow a fixed activity trace through contrasting sections and an ending. It supports listening and tuning separately from maze generation.

## Score and instrument editing

`Assets/Resources/Orchestra/ScoreRules.asset` contains the four eight-bar scale-degree progression templates per mode, motif and section energy settings. The composer develops the motif across eight-bar phrases and four-section forms, assigning parts to strings, brass, woodwinds and percussion. It groups solver events into at most two musical accents per beat.

`InstrumentBank.asset` points to `manifest.json`. Each manifest entry supplies a resource, instrument, root MIDI note, velocity layer, alternate take and optional tuning correction. Source filenames use C3 for MIDI 60. Timpani tuning is corrected separately. Optional loop frame bounds must be validated against the PCM recording; the current bank uses natural recordings and rearticulation, without fabricated sustain loops.

Samples load before playback. At most four seconds of each recording are decoded, keeping the bank under 256 MiB of PCM. Short-note recordings remain intact; the sampler fades at natural ends. The renderer uses 96 fixed voices, sample-timed note starts, stereo panning, a damped shared hall and a stereo-linked peak limiter. Musical composition runs at beat boundaries on the audio thread using fixed storage, so main-thread stalls cannot disrupt the beat. The main thread sends bounded state messages and never edits active voices.

Regenerate the sample bank with `python3 scripts/prepare_orchestra.py`. This uses Python's standard library and curl, downloads from a pinned source revision and records source paths and SHA-256 hashes. It moves superseded generated samples into the ignored build cache. It does not require pip packages.

## Sample credits

VSCO 2 Community Edition by Versilian Studios and its contributors. Source: <https://github.com/sgossner/VSCO-2-CE>. Revision: `440300901dfe9275fd84e0b7763af1f8443ae62e`. Licence: CC0 1.0, included at `Assets/Resources/Orchestra/LICENSE.txt`. Original recordings are retained in the bank; tuning and note envelopes are applied at playback.

The pre-existing soundtrack recordings retain their original provenance. This change does not add them to the CC0 sample licence.
