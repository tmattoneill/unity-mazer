# Music roadmap

## Style

- [x] Add a **Style** selector and name the current sound **Cinematic**. Keep it as the default, including its repeating string patterns, orchestral layers, brass themes and percussion. Keep recorded Soundtrack selection separate.
- [x] Add **Ambient**: slowly changing harmony, sustained textures, sparse motifs, long releases, evolving timbre and gentle dynamics. Allow passages without a strong beat. More live paths should add depth and colour without forcing heavy percussion.
- [x] Add **EDM**: a firm kick and bass foundation, repeating hooks, syncopated parts, layered percussion, filter movement, builds, breakdowns and drops. Use programmed synth voices or suitable instrument samples, with controlled low end and rhythmic ducking. Pace these sections against the solve sheet.
- [x] Add **Classical**: clear phrases, thematic development, counterpoint, voice leading, dynamic contrast and prepared cadences. Give strings, woodwinds and brass distinct musical roles, with accompaniment shaped around the theme.
- [x] Give each style its own composition, arrangement, sound palette and production rules. Preserve seeded variation, key selection, live maze response and WAV saving. Keep generation procedural and rules-based. Carry the style name into recording filenames.

## Live paths drive the ensemble

- [x] Make the number of audible instrumental parts follow the number of **currently active paths**, using the live solver's active-agent count. More live paths should produce more instruments and a bigger sound; fewer should bring the ensemble back down. Do not use the Target Paths setting or total agents ever spawned as this signal.
- [x] Define a progression from a small core ensemble to the full arrangement for each style. Add distinct musical roles before doubling existing parts. Map large path counts into a bounded number of layers, with Density controlling the overall ceiling.
- [x] Smooth the path-count signal and use separate entry/exit thresholds so rapid births and deaths do not make parts flicker. Bring parts in and out at suitable beat or phrase boundaries, with musical releases and enough immediacy to hear the connection to the maze.
- [x] Check sparse, growing, crowded and collapsing path populations. Increasing paths should audibly widen the ensemble while preserving the theme, mix headroom and audio processing budget.

## Presolve and pace the ending

- [x] Presolve the generated maze before playback and create a **solve sheet** indexed by solver step: active-path count, branch births/deaths, coverage, major turning points, outcome and the exact terminal step. Use the same traversal and agent-order rules as the visible solver. A shortest-path calculation alone will not predict this solver's run.
- [x] Make traversal order explicit and repeatable. Verify that the presolve and live run produce matching traces, including simultaneous branch events and the first successful exit. Avoid consuming or changing the music seed during presolve.
- [x] Benchmark presolving across supported maze sizes, targeting milliseconds. The current solver allocates lists and copies agent paths; profile these costs and add a lightweight analysis mode with shared traversal logic if needed. Record measured time and memory rather than assuming the current implementation is fast enough.
- [x] Compare live progress with the solve sheet throughout playback. Estimate remaining time from remaining steps and observed step timing, accounting for pauses, speed changes and frame-rate limits. The current coroutine's requested delay alone is not a reliable clock. Detect trace divergence and refresh the forecast if necessary.
- [x] Use the forecast to plan the piece's opening, development, build, release and final cadence. Start preparing the ending before the exit is reached, so the musical resolution lands with the solve. Adapt through phrase length, accompaniment changes and bounded tempo adjustments; preserve responsive maze playback. Handle very short runs with a compact musical form.
- [x] Test very short and long mazes, exhausted runs, sudden speed changes, pauses near the exit, resets and restarts. Measure the gap between visual completion and musical resolution. Keep a graceful fallback cadence when the forecast becomes unreliable, and retain the reverb tail in saved WAVs.
