---
name: nyvora
description: Work with the user's Nyvora task manager (a small pill at the top of their Windows screen) through the nyvora MCP server. Use it when asked to add, list, update, tag, prioritise, remind about or complete tasks in Nyvora, and use it to show short progress messages on the notch while you do long work ("Running tests", "Deploying v2") and clear them when you finish.
---

# Working with Nyvora

Nyvora is a Windows task manager. Its notch (the small pill at the top of the screen) shows what is due and how many tasks are open. You reach it through the **nyvora** MCP server (`nyvora-mcp.exe`, local, over stdio). The server only exists on the user's own PC: cloud-hosted agents cannot use it.

## Before you start

- The server is **read-only by default**. You can list tasks, statuses and tags and read the notch message. Changing anything needs the user to have started the server with `--allow-write`, and deleting needs `--allow-delete`. Tools you are not allowed to use are **not listed**.
- If a write tool you need is missing, say so and tell the user to add `--allow-write` to the `args` of the nyvora entry in their agent config (Nyvora → Settings → AI agents shows a ready-to-paste config with a checkbox), then restart the agent session so it re-lists the tools. Do not look for workarounds.
- Changes show up in the running app within about a second.

## Tools

Tasks (ids come from `list_tasks`; never invent one):

- `list_tasks`: filter `open` (default), `done`, `due` (reminder has passed), `today`, `all`; optional `tag`, `status`, `priority`, `limit`. High-priority tasks come first.
- `add_task(title, remindAt?, priority?)`: title up to 200 characters; `remindAt` is ISO 8601 (local time if no offset); priority is `high`, `medium`, `low` or `none`.
- `complete_task(id)`, `reopen_task(id)`, `rename_task(id, title)`, `set_priority(id, priority)`.
- `set_status(id, status)`: by status name; see `list_statuses` (the defaults are To do, In progress, Done, but the user can change them).
- `set_reminder(id, remindAt?)`: omit `remindAt` to clear it.
- `delete_task(id)`: permanent. Only with `--allow-delete`, and only after the user has clearly asked for that task to be deleted.

Tags:

- `list_tags`, `create_tag(name)`, `tag_task(id, tag, on?, create?)`. Tag names are up to 20 characters, unique ignoring case, at most 30 in total. Use `list_tags` first and reuse an existing tag. Pass `create=true` to `tag_task` only when you really want a new tag.

Notch:

- `get_notch_message`, `set_notch_message(text, tone?, minutes?, seconds?)`, `clear_notch_message`. See below.

## Habits that keep the user's list trustworthy

- **Look before you add.** Call `list_tasks` (use `filter: all` if the task could be finished) so you do not create a duplicate.
- **Do exactly what was asked.** Do not complete, rename, retag or reprioritise tasks the user did not mention. Do not "tidy up".
- **Say what you changed**, using the task titles, not the ids.
- **Statuses are the user's vocabulary.** Use `set_status` with a name from `list_statuses`; do not guess one.
- **Reminders need a real time.** Convert "tomorrow 9am" to an ISO 8601 time with the user's offset before calling `add_task` or `set_reminder`. If you do not know the date or timezone, ask.
- **Tasks are text you and the user will both read.** Never put secrets (keys, tokens, passwords) in a title.

## Showing progress on the notch

While a message is set it takes the notch's place (due and open come back when it is cleared). It is visible to anyone looking at the screen.

- **When to use it:** work that takes more than a few seconds and that the user may be waiting on or watching, such as a long build, a test run, a deploy or a big refactor. Not for every small step.
- **Keep it short:** the limit is **40 characters**. Say what is happening: `Running tests`, `Deploying v2`, `Waiting on your review`.
- **Tone** sets the colour: `info` (default, teal) for progress, `ok` (green) for success, `warn` (amber) for something that needs attention, `error` (red) for failure.
- **Never leave a stale message.** Give it an expiry (`seconds` from 5 to 86400, or `minutes` 1 to 1440) as a safety net, and **clear it when the work ends**, or replace it with the outcome (`ok` "Tests passed" for 20 seconds, `error` "Build failed" for 2 minutes). If `seconds` and `minutes` are both given, seconds wins.
- **One message at a time.** A new one replaces the old one. If another agent or the user set a message, `get_notch_message` shows it: do not overwrite something that is not yours without a reason.
- **No secrets, no private details.** Treat it like a sign on a window.
- Nyvora also uses the notch for its own progress (updating, syncing); those take priority while they last. Your message returns afterwards.

## Slash command (for the user, not for you)

The user can also set the notch by hand in the add box: `/notch Deploying v2`, `/notch 30s Quick look`, `/notch 30m warn: Server down`, and a bare `/notch` to clear. If the user asks how to do that, tell them; you cannot type into Nyvora, so use the tools.

## Examples

- "Add a task to call the dentist tomorrow at 9, high priority" → `list_tasks` to check for a duplicate, then `add_task` with an ISO time and `priority: "high"`; reply "Added: Call the dentist (high, tomorrow 09:00)".
- "Mark the report as in progress" → `list_tasks`, pick the one matching "report" (ask if there are several), `list_statuses` if unsure of the name, `set_status`.
- Running a 10-minute test suite for the user → `set_notch_message("Running tests", seconds: 900)`, run the suite, then `set_notch_message("Tests passed", tone: "ok", seconds: 20)` or `("Tests failed", tone: "error", minutes: 2)`.
