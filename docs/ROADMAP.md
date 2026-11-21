# Roadmap

1. **Foundation / visual / window integration** — implemented in the first milestone; awaiting verification on Windows.
2. **Reliability** — Core unit tests exist; add more manual-run evidence, then consider UI automation.
3. **CI** — `.github/workflows/windows-build.yml` builds, tests, and publishes a *development* artifact (self-contained, unsigned, unpackaged).
4. **Experimental track** (see below) — agent activities, rule-based proactive nudges, an optional AI layer.
5. **Later, by usefulness** — notification summaries, quick actions, system info, media controls, clipboard utilities, user-configurable appearance.

## Experimental track

Rule for all of it: everything lives behind one master flag, `ExperimentalFeatures` in `settings.json` (default **off**). With it off Nyvora behaves exactly as it does today and the experimental MCP tools are hidden and refused. Deterministic rules decide *when* the pill interrupts; AI, when enabled, only writes or classifies.

| Phase | What | Status |
|---|---|---|
| 0 | Run the Windows manual checklist below; log failures | Pill confirmed running on Windows (screenshots); full checklist still unticked |
| 1 | **Activity channel**: agents publish named, multi-step activities (`publish_activity`, `list_activities`, `clear_activity` MCP tools; `activities.json`; expiry so nothing lingers; approval question the agent can read back) | Core + MCP + tests done. **Pill/panel UI not built yet**, and no UI to flip the flag |
| 2 | Rule engine for proactive nudges (extend `RecurringNudge` / `NotchSuggestions`); rate limits, per-rule mute, no new sensors without a per-signal opt-in | Not started |
| 3 | Optional AI layer: `IAiProvider` in Core; OpenAI-compatible (covers Ollama and cloud) and Anthropic providers; one-time setup wizard; key in Windows Credential Locker; per-feature toggles; always falls back to plain text | Not started |
| 4 | One-click built-in local model (evaluate Foundry Local; Windows AI APIs need package identity, which the unpackaged build lacks) | Not started |

Open: the flag has no Settings toggle yet (edit `"ExperimentalFeatures": true` in `%APPDATA%\Nyvora\settings.json`); activities are not shown in the app until the Phase 1 UI lands.

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
