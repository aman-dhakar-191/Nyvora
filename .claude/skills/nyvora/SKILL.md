---
name: nyvora
description: Work with the user's Nyvora task manager (a small pill at the top of their Windows screen) through the nyvora MCP server. Use it when asked to add, list, update, tag, prioritise, remind about or complete tasks in Nyvora, and use it to show short status messages on the notch ("Tests passed", "Deploying v2") while you work, with a timer so they never linger.
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

- `get_notch_message`, `set_notch_message(text, tone?, minutes?, seconds?, keep?)`, `clear_notch_message`. See below.

## Habits that keep the user's list trustworthy

- **Look before you add.** Call `list_tasks` (use `filter: all` if the task could be finished) so you do not create a duplicate.
- **Do exactly what was asked.** Do not complete, rename, retag or reprioritise tasks the user did not mention. Do not "tidy up".
- **Say what you changed**, using the task titles, not the ids.
- **Statuses are the user's vocabulary.** Use `set_status` with a name from `list_statuses`; do not guess one.
- **Reminders need a real time.** Convert "tomorrow 9am" to an ISO 8601 time with the user's offset before calling `add_task` or `set_reminder`. If you do not know the date or timezone, ask.
- **Tasks are text you and the user will both read.** Never put secrets (keys, tokens, passwords) in a title.

## Showing status on the notch

While a message is set it takes the notch's place (due and open come back when it goes away). It is visible to anyone looking at the screen.

- **With no time, it is brief.** A message with no `seconds`, `minutes` or `keep` shows for **5 seconds** and then goes away by itself. That is right for a quick ping: `Tests passed`, `Saved`, `Build failed`. You do not need to clear it.
- **For something that lasts, give it a time** that covers the work: `seconds` (5 to 86400) or `minutes` (1 to 1440). If both are given, seconds wins. Use a time slightly longer than you expect the work to take, so it cannot outlive the work by much.
- **`keep: true` pins it** until `clear_notch_message`. Use it rarely, only when you cannot predict how long something will take, and **always clear it when your work ends** (or replace it with the outcome). A pinned message left behind is the one thing to avoid.
- **Keep it short:** the limit is **40 characters**. Say what is happening: `Running tests`, `Deploying v2`, `Waiting on your review`. Do not use it for every small step.
- **Tone** sets the colour: `info` (default, teal) for progress, `ok` (green) for success, `warn` (amber) for something that needs attention, `error` (red) for failure.
- **One message at a time.** A new one replaces the old one. If another agent or the user set a message, `get_notch_message` shows it: do not overwrite something that is not yours without a reason.
- **No secrets, no private details.** Treat it like a sign on a window.
- Nyvora also uses the notch for its own progress (updating, syncing); those take priority while they last. Your message returns afterwards if it has not expired.

## Slash command (for the user, not for you)

The user can also set the notch by hand in the add box: `/notch Deploying v2` (shows for 5 seconds), `/notch 30s Quick look`, `/notch 30m warn: Server down`, `/notch pin Deploying v2` (stays until cleared), and a bare `/notch` to clear. If the user asks how to do that, tell them; you cannot type into Nyvora, so use the tools.

## Examples

- "Add a task to call the dentist tomorrow at 9, high priority" → `list_tasks` to check for a duplicate, then `add_task` with an ISO time and `priority: "high"`; reply "Added: Call the dentist (high, tomorrow 09:00)".
- "Mark the report as in progress" → `list_tasks`, pick the one matching "report" (ask if there are several), `list_statuses` if unsure of the name, `set_status`.
- A quick result → `set_notch_message("Tests passed", tone: "ok")`: shows for 5 seconds, no cleanup needed.
- A 10-minute test run the user is waiting on → `set_notch_message("Running tests", seconds: 900)`; when it ends, `set_notch_message("Tests failed", tone: "error", minutes: 2)` or `clear_notch_message`.
