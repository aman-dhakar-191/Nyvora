# Roadmap

1. **Foundation / visual / window integration** — implemented in the first milestone; awaiting verification on Windows.
2. **Reliability** — Core unit tests exist; add more manual-run evidence, then consider UI automation.
3. **CI** — `.github/workflows/windows-build.yml` builds, tests, and publishes a *development* artifact (self-contained, unsigned, unpackaged).
4. **Later, by usefulness** — notification summaries, quick actions, system info, media controls, clipboard utilities, optional AI, user-configurable appearance.

## Manual verification checklist (Windows 10/11)

- [ ] Launches; pill appears centered ~8 DIP below the top of the primary display; no taskbar button.
- [ ] Launching does not steal focus from the foreground app.
- [ ] Click expands smoothly; content fades in without reflow; Esc / Collapse / click-away collapse.
- [ ] Click the pill 20x rapidly; spam Esc; reverse mid-animation — no crash, no stuck state.
- [ ] Right-click → Quit exits; no leftover process.
- [ ] Move to a 150%/200% DPI monitor or change scaling — pill stays centered and sized correctly.
- [ ] Change resolution / unplug primary display — pill re-centers.
- [ ] Clicking the desktop area beside the pill reaches the desktop/other windows.
- [ ] `%LOCALAPPDATA%\Nyvora\nyvora.log` has no unexpected errors.

## Known limitations

- **Not yet run on Windows.** The first build/launch happens in CI (build + unit tests only); graphical and native behavior is unverified until the checklist above is done.
- Rounded edges are region-clipped (aliased); the 1px border is clipped at corners.
- Always-on-top does not appear over exclusive-fullscreen apps; no fullscreen handling.
- Primary display only; no per-monitor choice.
- Self-contained unsigned unpackaged build: not an installer, not clean-machine tested.
- Items to verify: `Show(false)` rendering before any `Activate()`, `WS_POPUP` conversion at runtime, `Activate()` on a `WS_EX_NOACTIVATE` window.
