# Memory audit

Last updated: 7 September 2026.

## Current result

The first static and standalone pass found two real lifetime bugs. `MazeRenderer` replaced native textures and sprites without destroying them. `AudioCommandQueue` left consumed commands in its ring, which could retain up to 255 old `SessionRecording` objects. Each recording owns a four-second stereo float buffer of about 1.35 MiB at 44.1 kHz or 1.46 MiB at 48 kHz.

Both bugs are fixed. The renderer now destroys its runtime assets on replacement and teardown. The queue clears each slot before advancing its read index. The solve-tree texture also has explicit teardown.

The nine recorded soundtracks previously used Decompress On Load. They now stream from disk, load in the background and do not preload. Changing tracks stops and unloads the old clip. The 181.2 MiB procedural orchestra sample bank remains resident by design because the audio callback needs immediate access to its PCM.

Post-fix player figures remain pending until `BuildMacMemoryAudit` and the full audit wrapper complete. Do not treat the limits below as measured results.

## Baseline

The pre-fix macOS player was measured on a 24 GB MacBook Air M4 running macOS 26.6.2. At a Retina Metal surface of 3420 by 2146 pixels it reached about 776.1 MiB physical footprint after startup and a 1.4 GiB startup peak. A separate `ps` sample reported about 446 MiB RSS.

The graphics share was large: about 171 MiB of resident IOAccelerator memory, 85 MiB of IOSurface memory and 155 MiB of owned unmapped graphics memory. Retina remains enabled for the first fixed measurement.

Incident `CB46C6B2-489B-4E5A-8227-4D7CC9F2F78F` is excluded. That process aborted roughly 70 ms after launch in `HIServices._RegisterApplication`, before Unity initialised. Its 6.3 GiB VM total includes Unity's normal 4 GiB entity-ID address reservation and does not show physical use. The audit wrapper opens the app through LaunchServices to avoid direct-executable launch failures.

## Ownership and lifetime

| Memory | Owner | Intended lifetime | Release point |
| --- | --- | --- | --- |
| 181.2 MiB instrument PCM | `MazeAudioEngine` instrument bank | Application | Process exit |
| Four-second recording ring | `SessionRecording` | One orchestral take | Worker completion and reference release |
| Maze texture and sprite | `MazeRenderer` | Current generated maze | Replacement or component teardown |
| Solve-tree texture | `SolveTreeRenderer` | Solve-tree component | Component teardown |
| Recorded soundtrack decode and stream buffers | Current `AudioClip` | Selected soundtrack session | Track change, mode change, stop or teardown |
| Solver paths and solve sheet | `MazeGameManager` | Current run | Reset or next generation |

## Other changes

The solver reuses its four-entry neighbour buffer. The solve-tree view pools child lists. The vitals panel reuses its string builder and slider list. Synthetic clap generation no longer creates an array for every generated sample. Instrument loading unloads the Unity source clip even when decoding or validation throws. Reset now drops the grid, solver, solve sheet and conductor references.

Recording workers have an explicit live count for diagnostics. Startup removes stale temporary takes left by an earlier crash. The development audit checks that workers and temporary WAV files return to baseline.

## Automated audit

Build the development player:

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity
"$UNITY" -batchmode -nographics -quit -projectPath "$PWD" \
  -executeMethod MazeSolver.Editor.BuildOrchestra.BuildMacMemoryAudit \
  -logFile /tmp/unity-memory-audit-build.log
```

Run the macOS audit:

```sh
scripts/run_memory_audit_macos.sh
```

The wrapper performs five cold LaunchServices starts and one full run. The full run covers 100 maximum-size maze replacements, 50 recording cancellations, two passes through every soundtrack and 25 audio renderer rebuilds. It writes Unity memory checkpoints as JSON, one-second RSS samples and periodic `vmmap` summaries under `Builds/MemoryAudit/reports`.

The runner exists only in Editor and Development builds. Release players expose no audit entry point.

## Gates

- Settled managed or Unity allocation drift must stay within 16 MiB for each repeated scenario.
- Texture and sprite counts must remain fixed after the warm-up maze.
- Recording worker and temporary WAV counts must return to zero.
- No soundtrack may remain loaded after soundtrack mode stops.
- The procedural audio callback must allocate zero managed bytes.
- The target settled physical footprint is below 700 MiB. The target startup physical peak is below 1.0 GiB.
- Five cold LaunchServices launches must produce five JSON reports and no macOS crash report.

If the fixed Retina build misses the physical-memory gates, test Metal framebuffer-only mode and the Low standalone quality preset next. Keep Retina until those two changes have been measured.

## Remaining risks

Agent path copies at forks can create large short-lived managed allocations in dense 100 by 100 mazes. They are bounded by one run and clear on reset. Redesign them only if the audit shows sustained allocation above 1 MiB per second or a gate failure.

Windows and Linux have the same managed lifetime rules. Their process-level sampling scripts remain blocked until the matching Unity build-support modules are installed.
