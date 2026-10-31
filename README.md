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

- Click the pill to expand. It opens a task list: type a task and press Enter. To give the next task a reminder, click the ⏰ button next to the input first (10 min, 1 hour, tomorrow 9:00, or a time); the button shows what is set and resets after the task is added. Due tasks turn the resting pill amber ("N due"); the pill also pops open, without taking focus, when a reminder fires. Snooze adds 10 minutes. Tasks are stored locally in `%APPDATA%\Nyvora\tasks.json`; reminders only fire while Nyvora is running (missed ones fire at next start).
- **Settings** (gear icon in the panel header) open as a panel that slides out to the right of the pill, so your tasks stay visible; close it with its ✕ or the gear. It has **Always on top** (default on), the hourly status reminder, **Check for updates** / **Install and restart**, and the task-status editor. All saved in `settings.json`.
- **Always on top:** when on (default) Turn it off and the pill behaves like a normal window and can sit behind others; expanding it brings it forward. Exclusive-fullscreen apps and games cover the pill either way.
- **Reminders on existing tasks:** every open task has a button showing its reminder ("⏰ 17:00", "Due 14:30") or "⏰ Add". Click it to set, change or clear the reminder: 10 min, 1 hour, tomorrow 9:00, or a time such as `14:30` or `2:30 pm` (next occurrence, so a time already past today means tomorrow).
- **Resize:** with the pill open, drag the grip in its bottom-right corner to resize it (it stays centered under the top edge); double-click the grip to reset. The size is saved in `settings.json` and used next time. The minimum is 400×320 so the controls fit; the maximum is your screen.
- **Editing and copying:** double-click a task's title to edit it (Enter or clicking away saves, Esc cancels). Titles are selectable: drag to select, Ctrl+C to copy. Hover a row to see its whole title wrapped.
- **Tags:** every open task has a `#` button. Click it to tick the tags the task uses, or type a new tag name and press Enter/Add (it is created and put on that task). Tags show as colored chips under the task. Rename, recolor (click the color dot) or delete tags in Settings; deleting a tag removes it from all tasks. Up to 30 tags, names up to 20 characters, unique ignoring case. Tags are stored in `settings.json`; each task keeps its tag ids in `tasks.json`.
- **Statuses:** every task has a status (default: To do, In progress, Done). Click a task's colored status chip to move it to the next one; the checkbox finishes or reopens it. The gear icon in the panel header opens the status editor: rename, reorder, add (up to 10) or remove statuses, and tick **Done** on the ones that finish a task. At least one status must count as done and one as not done. Tasks in a removed status move to the first open (or first done) status. Stored in `settings.json`.
- **Hourly status reminder:** once an hour, if you have open tasks, the pill opens without taking focus and shows "Time to update your task statuses". Turn it off in Settings; stored as `StatusReminderMinutes` in `settings.json` (0 = off). It counts from when Nyvora starts, so it never fires right at launch, and after sleep you get one reminder, not several.
- **Resting pill:** shows `N due · M open` (amber dot while reminders are due), `M open`, or just `Nyvora` when there is nothing open.
- Collapse with **Esc**, **Collapse**, or by clicking elsewhere.
- Right-click the pill (or use **Quit**) to exit. There is no taskbar entry by design.
- Updates: Settings shows the version and a **Check for updates** button, and the panel footer shows "Update vX.Y.Z available" when one is waiting. Nyvora also checks GitHub Releases once a day, a few seconds after launch (set `"AutoCheckUpdates": false` in `%APPDATA%\Nyvora\settings.json` to disable all automatic network requests). Installing is always your click: it downloads the release zip, verifies its SHA-256 from the release's `.sha256` file, then restarts into the new version. It is disabled when running from a build folder.
- Logs: `%LOCALAPPDATA%\Nyvora\nyvora.log` (events and exceptions only; no network, no telemetry).

## Releasing

In GitHub go to **Actions → Release → Run workflow** and choose **patch**, **minor** or **major**. The workflow takes the latest `vX.Y.Z` tag, bumps it (patch `1.2.3→1.2.4`, minor `1.2.3→1.3.0`, major `1.2.3→2.0.0`), runs the tests, builds the app with that version, tags the selected commit, and publishes a pre-release with the zip and its `.sha256`. Installed copies then offer it through **Check for updates**. Pushing a tag such as `v1.2.4` by hand releases exactly that version instead.

## Docs

- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap and manual verification](docs/ROADMAP.md)

## Known limitations

See [docs/ROADMAP.md](docs/ROADMAP.md#known-limitations). In short: the visual and native-window behavior was written without being run on Windows yet and needs the manual checklist; edges are clipped with a window region (not anti-aliased); only the primary display is used.

## Reminders, hover and hierarchy

- **Reminder picker** (the ⏰ beside the input, or the reminder button on a task): quick choices (in 10 min, in 1 hour, this evening, tomorrow 9:00, next Monday 9:00, clear), a box that understands plain words with a live preview, and the time picker behind "Pick a time…". The box takes `in 2h`, `30 min`, `1 day`, `tomorrow 9am`, `tonight`, `fri 5pm`, `next monday`, `nov 2 3pm`, `2 nov`, `2026-11-05 14:00` or just `14:30`; the result is always in the future (`fri 9am` on a Friday afternoon means next Friday). Enter sets it, Esc closes the picker.
- **Hover:** the row highlight and its action buttons fade in. A title that is cut off expands to show everything after the pointer rests on the row for a moment; a title that already fits never changes, and sweeping the mouse down the list does not make rows jump.
- **Hierarchy:** overdue reminders are an amber pill, high-priority titles are bold, finished tasks are dimmed.

## Priority and search

- **Priority:** each open task has a flag button (visible on hover). One click steps through none, high, medium, low. A colored dot in front of the title shows it (red, amber, blue). Within the same status, higher priority is listed first; an In progress task still sits above a To do task of any priority.
- **Search:** the magnifier in the header, or Ctrl+F. Every word must match the title, status or a tag. `#work` matches tag names only, `!high` matches the priority only. Esc clears the text, a second Esc closes the search.
- **For AI agents:** `add_task` takes an optional `priority`, `set_priority` changes it, `list_tasks` can filter by it and every task shows its priority.

## Installing

Download `Nyvora-Setup-vX.Y.Z-x64.exe` from the latest release and run it. It installs **for your user only, with no admin rights**, into `%LOCALAPPDATA%\Programs\Nyvora`, adds a Start menu entry (and, if you tick it, a desktop shortcut), and offers to start Nyvora. Uninstall from Windows Settings → Apps, or run `unins000.exe` in the install folder. The installer is **not code-signed**, so Windows SmartScreen may show "unknown publisher": choose More info → Run anyway. The zip is still published for portable use, and it is what the in-app updater downloads; self-updates replace files in the install folder in place.

**Where things live**
- Program files: `%LOCALAPPDATA%\Programs\Nyvora` (installer) or wherever you unzipped.
- Your data, in Roaming: `%APPDATA%\Nyvora\tasks.json` and `settings.json`. Install, update and uninstall never touch them. Versions before this one kept them in `%LOCALAPPDATA%\Nyvora`; on first start they are **copied** to Roaming (never moved or deleted: the old files stay as a backup, and if the copy fails Nyvora keeps using the old folder so nothing disappears). Close all running Nyvora copies before updating, because an old copy keeps writing to the old folder.
- Disposable and machine-specific files, in Local: `%LOCALAPPDATA%\Nyvora\nyvora.log`, `updates\` and `rollback\`.

## Notch messages

The pill shows what is due and how many tasks are open. It can also show a **temporary status or message** that comes and goes: while the message lasts it takes the place of due and open (hover the pill to see the counts), the pill widens if the text needs the room, and when the message is cleared or expires, due and open come back.

- **From the add box:** `/notch Deploying v2` shows it, `/notch warn: Server down` shows it in amber (tones: `info`, `ok`, `warn`, `error`), and a bare `/notch` clears it.
- **From an AI agent:** the MCP server has `set_notch_message` (text up to 40 characters, optional `tone`, optional `minutes` after which it disappears by itself), `clear_notch_message` and `get_notch_message`. The two that change the notch need `--allow-write`, like the task tools.
- It is stored in `notch.json` next to your tasks, and the running app picks changes up within a second.

## Keyboard

| Keys | Does |
|---|---|
| Ctrl+Alt+N (default) | From any app: open Nyvora with the cursor in the add box. Settings → General lets you turn it off or press a different combination (Ctrl, Alt or Win plus a letter, digit or F-key); if another app already owns it, Nyvora says so and keeps the old one |
| Ctrl+N | Jump to the add box (leaves search) |
| Ctrl+F | Search |
| Ctrl+M | Compact layout: a narrow list with a colored status dot instead of the status name, and the controls on a line under each title (fixed size, so the resize grip is off) |
| Ctrl+Z | Bring back the task you just deleted (not while typing in a box, where it undoes typing) |
| Esc | Cancel an edit, close search, or collapse |

Closing animates ease-in/ease-out; with Windows animations turned off, the pill changes size instantly.

## Shortcuts and single instance

Settings → Shortcuts adds or removes a **desktop shortcut**. Windows does not let apps pin themselves to the **taskbar**, so the section links to the Start menu entry (created at every launch) where you can right-click Nyvora and choose Pin to taskbar. Only one copy runs at a time: starting Nyvora again (a shortcut, launch at sign-in, a manual start) opens the running pill instead of creating a second one.

## AI agents (MCP server)

Nyvora ships `nyvora-mcp.exe` next to `Nyvora.exe`: a [Model Context Protocol](https://modelcontextprotocol.io) server over stdio, so any MCP-capable AI app (Claude Desktop, Cursor, VS Code, Claude Code...) can work with your tasks. Settings → "AI agents (MCP)" shows a ready-to-paste config with the right path and a Copy button.

- **Read-only by default.** Tools: `list_tasks` (filters: open, done, due, today, all; tag; status), `list_statuses`, `list_tags`, `get_notch_message`.
- **`--allow-write`** (add it to the `args` in the agent's config) enables `add_task`, `complete_task`, `reopen_task`, `set_status`, `set_reminder`, `rename_task`, `tag_task`. **`--allow-delete`** additionally enables `delete_task`. Tools that are not allowed are not listed, and calling one returns an error.
- Statuses are read from `settings.json`; the server cannot change them. It **can create tags** (`create_tag`, or `tag_task` with `create=true`, both need `--allow-write`): the new tag is saved in `settings.json` and the running app shows it within a second.
- Local only: it talks over stdin/stdout, opens no network port, and sends nothing anywhere.
- Changes made by an agent appear in the running app within about half a second (the app watches `tasks.json`). A cross-process lock keeps the two from interleaving, but if you edit a task in the app at the very same moment an agent edits the file, the later save wins.
- Task titles are text an agent will read; do not connect an agent you do not trust, and think before enabling write access for one that also reads untrusted content.
- `--data-dir PATH` points it at another data folder (used by CI).

