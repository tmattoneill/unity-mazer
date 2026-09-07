# Current work

The code roadmap is complete on `main`. The remaining work is listening, platform testing and release preparation.

## Next

- [ ] Build and run the new macOS memory audit, then enter the post-fix RSS, physical-footprint and drift results in `MEMORY_AUDIT.md`. The static fixes and audit harness are complete; the first instrumented player run remains.
- [ ] Listen to Cinematic, Ambient, EDM and Classical in the Unity editor across sparse, growing, crowded and collapsing mazes. Tune score tables, voice levels, reverb, low end, transitions and cadence timing where needed.
- [ ] After tuning, run `BuildOrchestra.PrepareAndValidate` and keep all presolve, composer, renderer, recording and timing checks green. Review the four generated comparison WAVs as part of the pass.
- [ ] Test the macOS standalone app on more than one machine or macOS version. Use the checklist in `README.md` and record the OS, CPU and audio device for each result.
- [ ] Cut a new version and replace the public build after listening and platform checks. The current `v0.1.0` tag points to `a0c6f2b` and predates the four-style, presolve, solve-panel and tooltip work on `main`.

## Blocked

- [ ] Build and test Windows and Linux players. `BuildOrchestra.BuildWindows` and `BuildOrchestra.BuildLinux` exist, but Unity 6000.6.0f1 has only `MacStandaloneSupport`. Install Windows Build Support (Mono) and Linux Build Support (Mono) through Unity Hub first.

## Completed on `main`

- [x] Fixed runtime maze texture and sprite leaks, stale recording references in the audio queue, soundtrack decode residency and several hot-path allocations. Added explicit recording and renderer cleanup plus a Development-build memory regression runner.
- [x] Added Cinematic, Ambient, EDM and Classical as distinct procedural styles while keeping recorded soundtracks separate.
- [x] Made live active-path counts drive bounded ensemble layers with smoothing, hysteresis and Density as the ceiling.
- [x] Added deterministic headless presolving, per-step solve sheets, trace-divergence detection and an observed-time forecast.
- [x] Added planned endings, compact fallbacks, seeded variation, all 24 major and minor keys, and stereo 24-bit WAV capture.
- [x] Added a universal ad-hoc-signed macOS build, cross-platform build entry points, a collapsible solve panel and hover help.
