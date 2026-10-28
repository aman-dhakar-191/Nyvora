# Nyvora

A lightweight Windows desktop companion with an animated dynamic-notch pill. It rests as a small pill at the top-center of the primary display and expands on click.

**Status:** initial development (first milestone: the animated pill). Not ready for general distribution.

## Requirements

- Windows 10 (1809+) or Windows 11
- .NET 8 SDK
- To build the app locally: Visual Studio 2022 17.8+ with the ".NET desktop development" workload (or the .NET SDK alone; WinUI 3 is restored from NuGet)

## Build, test, run

```powershell
dotnet restore Nyvora.sln -p:Platform=x64
dotnet build Nyvora.sln -c Release -p:Platform=x64
dotnet test tests/Nyvora.Tests/Nyvora.Tests.csproj -c Release
dotnet run --project src/Nyvora.App -c Release -p:Platform=x64
```

`Nyvora.Core` (state machine, animation math) and its tests are plain `net8.0` and also build and test on Linux/macOS: `dotnet test tests/Nyvora.Tests`.

## Using it

- Click the pill to expand. It opens a task list: type a task and press Enter, or add it with a reminder (10 min, 1 hour, tomorrow 9:00). Due tasks turn the resting pill amber ("N due"); the pill also pops open, without taking focus, when a reminder fires. Snooze adds 10 minutes. Tasks are stored locally in `%LOCALAPPDATA%\Nyvora\tasks.json`; reminders only fire while Nyvora is running (missed ones fire at next start).
- **Always on top** is a checkbox in the panel footer (default on; saved in `settings.json`). Turn it off and the pill behaves like a normal window and can sit behind others; expanding it brings it forward. Exclusive-fullscreen apps and games cover the pill either way.
- **Reminders on existing tasks:** every open task has a button showing its reminder ("⏰ 17:00", "Due 14:30") or "⏰ Add". Click it to set, change or clear the reminder: 10 min, 1 hour, tomorrow 9:00, or a time such as `14:30` or `2:30 pm` (next occurrence, so a time already past today means tomorrow).
- **Resize:** with the pill open, drag the grip in its bottom-right corner to resize it (it stays centered under the top edge); double-click the grip to reset. The size is saved in `settings.json` and used next time. The minimum is 400×320 so the controls fit; the maximum is your screen.
- **Statuses:** every task has a status (default: To do, In progress, Done). Click a task's colored status chip to move it to the next one; the checkbox finishes or reopens it. The gear icon in the panel header opens the status editor: rename, reorder, add (up to 10) or remove statuses, and tick **Done** on the ones that finish a task. At least one status must count as done and one as not done. Tasks in a removed status move to the first open (or first done) status. Stored in `settings.json`.
- **Hourly status reminder:** once an hour, if you have open tasks, the pill opens without taking focus and shows "Time to update your task statuses". Turn it off with the checkbox in the status editor (gear icon); stored as `StatusReminderMinutes` in `settings.json` (0 = off). It counts from when Nyvora starts, so it never fires right at launch, and after sleep you get one reminder, not several.
- Collapse with **Esc**, **Collapse**, or by clicking elsewhere.
- Right-click the pill (or use **Quit**) to exit. There is no taskbar entry by design.
- Updates: the panel footer shows the version and a **Check for updates** button. Nyvora also checks GitHub Releases once a day, a few seconds after launch (set `"AutoCheckUpdates": false` in `%LOCALAPPDATA%\Nyvora\settings.json` to disable all automatic network requests). Installing is always your click: it downloads the release zip, verifies its SHA-256 from the release's `.sha256` file, then restarts into the new version. It is disabled when running from a build folder.
- Logs: `%LOCALAPPDATA%\Nyvora\nyvora.log` (events and exceptions only; no network, no telemetry).

## Releasing

In GitHub go to **Actions → Release → Run workflow** and choose **patch**, **minor** or **major**. The workflow takes the latest `vX.Y.Z` tag, bumps it (patch `1.2.3→1.2.4`, minor `1.2.3→1.3.0`, major `1.2.3→2.0.0`), runs the tests, builds the app with that version, tags the selected commit, and publishes a pre-release with the zip and its `.sha256`. Installed copies then offer it through **Check for updates**. Pushing a tag such as `v1.2.4` by hand releases exactly that version instead.

## Docs

- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap and manual verification](docs/ROADMAP.md)

## Known limitations

See [docs/ROADMAP.md](docs/ROADMAP.md#known-limitations). In short: the visual and native-window behavior was written without being run on Windows yet and needs the manual checklist; edges are clipped with a window region (not anti-aliased); only the primary display is used.
