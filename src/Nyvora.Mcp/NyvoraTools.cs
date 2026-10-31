using System.Globalization;
using System.Text.Json.Nodes;
using Nyvora.Core.Settings;
using Nyvora.Core.Tasks;

namespace Nyvora.Mcp;

public enum ToolAccess
{
    Read,
    Write,
    Delete,
}

public sealed record ToolResult(string Text, bool IsError);

/// <summary>
/// The tools an AI agent can use. Every call loads tasks.json and settings.json fresh, applies one change under the
/// cross-process file lock, and saves atomically, so it composes with the running app (which reloads on change).
/// Tags and statuses are read from settings.json only; this server never edits settings.
/// </summary>
public sealed class NyvoraTools
{
    private sealed class State
    {
        public required TaskList Tasks { get; init; }

        public required AppSettings Settings { get; init; }

        /// <summary>Set when a tool created a tag: the tag list lives in settings.json, so the server saves it there.</summary>
        public bool TagsChanged { get; set; }
    }

    private sealed record Tool(string Name, string Description, ToolAccess Access, JsonObject Schema, Func<JsonObject, State, DateTimeOffset, string> Run);

    private readonly string _tasksPath;
    private readonly string _settingsPath;
    private readonly string _notchPath;
    private readonly bool _allowWrite;
    private readonly bool _allowDelete;
    private readonly Func<DateTimeOffset> _clock;
    private readonly List<Tool> _tools;

    public NyvoraTools(string dataDir, bool allowWrite, bool allowDelete, Func<DateTimeOffset>? clock = null)
    {
        _tasksPath = Path.Combine(dataDir, "tasks.json");
        _settingsPath = Path.Combine(dataDir, "settings.json");
        _notchPath = Path.Combine(dataDir, NotchMessageStore.FileName);
        _allowDelete = allowDelete;
        _allowWrite = allowWrite || allowDelete;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _tools = Build();
    }

    public bool AllowWrite => _allowWrite;

    private bool Allowed(Tool t) => t.Access switch
    {
        ToolAccess.Read => true,
        ToolAccess.Write => _allowWrite,
        _ => _allowDelete,
    };

    public JsonArray List()
    {
        var arr = new JsonArray();
        foreach (var t in _tools.Where(Allowed))
        {
            var def = new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = t.Schema.DeepClone(),
            };
            if (t.Access == ToolAccess.Read)
            {
                def["annotations"] = new JsonObject { ["readOnlyHint"] = true };
            }

            arr.Add(def);
        }

        return arr;
    }

    /// <summary>Null when no such tool exists (a protocol error for the caller to report).</summary>
    public ToolResult? Call(string name, JsonObject args)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == name);
        if (tool is null)
        {
            return null;
        }

        if (!Allowed(tool))
        {
            var flag = tool.Access == ToolAccess.Delete ? "--allow-delete" : "--allow-write";
            return new ToolResult($"'{name}' is disabled. The user must start nyvora-mcp with {flag} to allow it.", true);
        }

        try
        {
            var now = _clock();
            using var gate = TaskFileLock.Acquire(_tasksPath, TimeSpan.FromSeconds(5));
            if (!gate.Acquired)
            {
                return new ToolResult("Nyvora's task file is busy. Try again in a moment.", true);
            }

            var state = Load();
            var before = tool.Access == ToolAccess.Read ? null : Snapshot(state);
            var text = tool.Run(args, state, now);
            if (before is not null && Snapshot(state) != before)
            {
                new JsonTaskStore(_tasksPath).Save(state.Tasks.Items);
            }

            if (state.TagsChanged)
            {
                // Only the tag list changes; every other setting stays as it was read a moment ago.
                state.Settings.Tags = state.Tasks.Tags.Export();
                state.Settings.Save(_settingsPath);
            }

            return new ToolResult(text, false);
        }
        catch (ToolError ex)
        {
            return new ToolResult(ex.Message, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ToolResult("Could not read or write Nyvora's files: " + ex.Message, true);
        }
    }

    private static string Snapshot(State s) => System.Text.Json.JsonSerializer.Serialize(s.Tasks.Items);

    private State Load()
    {
        var settings = AppSettings.Load(_settingsPath);
        var statuses = new StatusSet(settings.Statuses);
        var tags = new TagSet(settings.Tags);
        var items = new JsonTaskStore(_tasksPath).Load();
        return new State { Tasks = new TaskList(items, statuses, tags), Settings = settings };
    }

    // ----- tool table -----

    private List<Tool> Build() => new()
    {
        new Tool("list_tasks",
            "List the user's tasks, most relevant first (in progress before to do, then higher priority, then soonest reminder). Each task has an id to use with the other tools.",
            ToolAccess.Read,
            Schema(
                ("filter", Str("open (default), done, due (reminder has passed), today (reminder is today), or all")),
                ("tag", Str("Only tasks with this tag name")),
                ("status", Str("Only tasks in this status name, e.g. 'In progress'")),
                ("priority", Str("Only tasks with this priority: high, medium, low or none")),
                ("limit", Int("Maximum tasks to return (default 50, max 200)"))),
            ListTasks),
        new Tool("list_statuses", "List the status names tasks can have (set in Nyvora's settings).", ToolAccess.Read, Schema(),
            (_, s, _) => Json(new JsonObject { ["statuses"] = new JsonArray(s.Tasks.Statuses.Items.Select(x => (JsonNode)new JsonObject { ["name"] = x.Name, ["done"] = x.IsDone }).ToArray()) })),
        new Tool("list_tags", "List the tag names that exist (created in Nyvora's settings).", ToolAccess.Read, Schema(),
            (_, s, _) => Json(new JsonObject { ["tags"] = new JsonArray(s.Tasks.Tags.Items.Select(x => (JsonNode)JsonValue.Create(x.Name)!).ToArray()) })),
        new Tool("get_notch_message", "Read the message currently shown on Nyvora's notch (the small pill), if any.", ToolAccess.Read, Schema(),
            (_, _, now) =>
            {
                var m = NotchMessageStore.Load(_notchPath, now);
                return Json(m is null
                    ? new JsonObject { ["message"] = null }
                    : new JsonObject { ["message"] = m.Text, ["tone"] = m.Tone.ToString().ToLowerInvariant(), ["expiresAt"] = m.ExpiresAt?.ToString("o", CultureInfo.InvariantCulture) });
            }),
        new Tool("set_notch_message",
            $"Show a short message on Nyvora's notch (up to {NotchMessage.MaxLength} characters) in place of the task counters, for example a status like 'Deploying v2'. Replaces any earlier message.",
            ToolAccess.Write,
            Schema(("text", Str($"The message, up to {NotchMessage.MaxLength} characters")),
                ("tone", Str("Optional colour: info (default), success, warning or error")),
                ("minutes", Int($"Optional: remove the message by itself after this many minutes (1 to {NotchMessage.MaxMinutes}); omit to keep it until cleared")),
                ("seconds", Int("Optional: like minutes but in seconds (5 to 86400), for a short message; wins over minutes if both are given"))).Require("text"),
            (a, _, now) =>
            {
                int? minutes = a["minutes"] is JsonNode n && n.GetValueKind() == System.Text.Json.JsonValueKind.Number ? (int)n : null;
                int? seconds = a["seconds"] is JsonNode sn && sn.GetValueKind() == System.Text.Json.JsonValueKind.Number ? (int)sn : null;
                var error = seconds is { } sec
                    ? NotchMessage.TryCreateTimed(RequireStr(a, "text"), OptionalStr(a, "tone"), TimeSpan.FromSeconds(sec), now, out var message)
                    : NotchMessage.TryCreate(RequireStr(a, "text"), OptionalStr(a, "tone"), minutes, now, out message);
                if (error is not null)
                {
                    throw new ToolError(error);
                }

                NotchMessageStore.Save(_notchPath, message!);
                return Json(new JsonObject { ["message"] = message!.Text, ["tone"] = message.Tone.ToString().ToLowerInvariant(), ["expiresAt"] = message.ExpiresAt?.ToString("o", CultureInfo.InvariantCulture) });
            }),
        new Tool("clear_notch_message", "Remove the message from Nyvora's notch so it shows the task counters again.", ToolAccess.Write, Schema(),
            (_, _, _) =>
            {
                NotchMessageStore.Clear(_notchPath);
                return Json(new JsonObject { ["message"] = null });
            }),
        new Tool("add_task", "Add a task. Optionally give a reminder time and a priority.", ToolAccess.Write,
            Schema(("title", Str("Task title, up to 200 characters")),
                ("remindAt", Str("Optional reminder as ISO 8601, e.g. 2026-10-30T09:00:00+05:30; local time if no offset")),
                ("priority", Str("Optional: high, medium, low or none (default none)"))).Require("title"),
            (a, s, now) =>
            {
                var priority = OptionalPriority(a);
                var item = s.Tasks.Add(RequireStr(a, "title"), now, OptionalTime(a, "remindAt"));
                if (item is not null && priority is { } p)
                {
                    s.Tasks.SetPriority(item.Id, p);
                }

                return Describe(item ?? throw new ToolError("The title is empty."), s, now);
            }),
        new Tool("set_priority", "Set a task's priority: high, medium, low or none.", ToolAccess.Write,
            Schema(("id", Str("Task id")), ("priority", Str("high, medium, low or none"))).Require("id", "priority"),
            (a, s, now) =>
            {
                var item = Find(a, s);
                s.Tasks.SetPriority(item.Id, OptionalPriority(a) ?? throw new ToolError("'priority' is required: high, medium, low or none."));
                return Describe(item, s, now);
            }),
        new Tool("complete_task", "Mark a task done.", ToolAccess.Write, Schema(("id", Str("Task id"))).Require("id"),
            (a, s, now) =>
            {
                var item = Find(a, s);
                if (!item.Done)
                {
                    s.Tasks.Toggle(item.Id);
                }

                return Describe(item, s, now);
            }),
        new Tool("reopen_task", "Reopen a finished task.", ToolAccess.Write, Schema(("id", Str("Task id"))).Require("id"),
            (a, s, now) =>
            {
                var item = Find(a, s);
                if (item.Done)
                {
                    s.Tasks.Toggle(item.Id);
                }

                return Describe(item, s, now);
            }),
        new Tool("set_status", "Move a task to another status by name (see list_statuses).", ToolAccess.Write,
            Schema(("id", Str("Task id")), ("status", Str("Status name"))).Require("id", "status"),
            (a, s, now) =>
            {
                var item = Find(a, s);
                var name = RequireStr(a, "status");
                var def = s.Tasks.Statuses.Items.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ToolError($"No status '{name}'. Available: {string.Join(", ", s.Tasks.Statuses.Items.Select(x => x.Name))}.");
                s.Tasks.SetStatus(item.Id, def.Id);
                return Describe(item, s, now);
            }),
        new Tool("set_reminder", "Set, change or clear (omit remindAt) a task's reminder.", ToolAccess.Write,
            Schema(("id", Str("Task id")), ("remindAt", Str("ISO 8601 time; omit to clear"))).Require("id"),
            (a, s, now) =>
            {
                var item = Find(a, s);
                s.Tasks.SetReminder(item.Id, OptionalTime(a, "remindAt"));
                return Describe(item, s, now);
            }),
        new Tool("rename_task", "Change a task's title.", ToolAccess.Write,
            Schema(("id", Str("Task id")), ("title", Str("New title"))).Require("id", "title"),
            (a, s, now) =>
            {
                var item = Find(a, s);
                if (!s.Tasks.Rename(item.Id, RequireStr(a, "title")) && string.IsNullOrWhiteSpace(RequireStr(a, "title")))
                {
                    throw new ToolError("The title is empty.");
                }

                return Describe(item, s, now);
            }),
        new Tool("create_tag", "Create a tag (see list_tags for the ones that exist). Names are up to 20 characters, unique ignoring case, at most 30 tags. Nyvora shows it right away.", ToolAccess.Write,
            Schema(("name", Str("Tag name"))).Require("name"),
            (a, s, _) =>
            {
                var existing = FindTag(s, RequireStr(a, "name"));
                var tag = existing is null
                    ? CreateTag(s, RequireStr(a, "name"))
                    : throw new ToolError($"The tag '{existing.Name}' already exists.");
                return Json(new JsonObject { ["tag"] = tag.Name, ["tags"] = TagNames(s) });
            }),
        new Tool("tag_task", "Add (or with on=false remove) a tag on a task. The tag must exist (see list_tags, or create_tag); pass create=true to make it in the same call.", ToolAccess.Write,
            Schema(("id", Str("Task id")), ("tag", Str("Tag name")), ("on", Bool("true to add (default), false to remove")), ("create", Bool("true to create the tag first if it does not exist (default false)"))).Require("id", "tag"),
            (a, s, now) =>
            {
                var item = Find(a, s);
                var name = RequireStr(a, "tag");
                var create = a["create"] is JsonNode c && c.GetValueKind() == System.Text.Json.JsonValueKind.True;
                var tag = FindTag(s, name)
                    ?? (create ? CreateTag(s, name) : throw new ToolError($"No tag '{name}'. Existing tags: {(s.Tasks.Tags.Items.Count == 0 ? "(none)" : string.Join(", ", s.Tasks.Tags.Items.Select(x => x.Name)))}. Use create_tag, or pass create=true."));
                var want = !(a["on"] is JsonNode on && on.GetValueKind() == System.Text.Json.JsonValueKind.False);
                if (item.TagIds.Contains(tag.Id) != want)
                {
                    s.Tasks.ToggleTag(item.Id, tag.Id);
                }

                return Describe(item, s, now);
            }),
        new Tool("delete_task", "Permanently delete a task. Cannot be undone.", ToolAccess.Delete, Schema(("id", Str("Task id"))).Require("id"),
            (a, s, _) =>
            {
                var item = Find(a, s);
                s.Tasks.Remove(item.Id);
                return Json(new JsonObject { ["deleted"] = item.Id.ToString(), ["title"] = item.Title });
            }),
    };

    private string ListTasks(JsonObject a, State s, DateTimeOffset now)
    {
        var filter = (OptionalStr(a, "filter") ?? "open").ToLowerInvariant();
        var tag = OptionalStr(a, "tag");
        var status = OptionalStr(a, "status");
        var priority = OptionalPriority(a);
        var limit = a["limit"] is JsonNode n && n.GetValueKind() == System.Text.Json.JsonValueKind.Number ? Math.Clamp((int)n, 1, 200) : 50;

        IEnumerable<TaskItem> q = s.Tasks.Ordered();
        q = filter switch
        {
            "open" => q.Where(t => !t.Done),
            "done" => q.Where(t => t.Done),
            "due" => q.Where(t => TaskList.IsDue(t, now)),
            "today" => q.Where(t => !t.Done && t.RemindAt is { } at && at.ToOffset(now.Offset).Date == now.Date),
            "all" => q,
            _ => throw new ToolError("filter must be one of: open, done, due, today, all."),
        };

        if (tag is not null)
        {
            q = q.Where(t => s.Tasks.TagsOf(t).Any(x => string.Equals(x.Name, tag, StringComparison.OrdinalIgnoreCase)));
        }

        if (status is not null)
        {
            q = q.Where(t => string.Equals(s.Tasks.StatusOf(t).Name, status, StringComparison.OrdinalIgnoreCase));
        }

        if (priority is { } want)
        {
            q = q.Where(t => t.Priority == want);
        }

        var all = q.ToList();
        return Json(new JsonObject
        {
            ["total"] = all.Count,
            ["tasks"] = new JsonArray(all.Take(limit).Select(t => (JsonNode)DescribeNode(t, s, now)).ToArray()),
        });
    }

    // ----- helpers -----

    private static string Describe(TaskItem item, State s, DateTimeOffset now) => Json(DescribeNode(item, s, now));

    private static JsonObject DescribeNode(TaskItem t, State s, DateTimeOffset now) => new()
    {
        ["id"] = t.Id.ToString(),
        ["title"] = t.Title,
        ["status"] = s.Tasks.StatusOf(t).Name,
        ["priority"] = TaskPriorities.Name(t.Priority),
        ["done"] = t.Done,
        ["tags"] = new JsonArray(s.Tasks.TagsOf(t).Select(x => (JsonNode)JsonValue.Create(x.Name)!).ToArray()),
        ["remindAt"] = t.RemindAt?.ToString("o", CultureInfo.InvariantCulture),
        ["due"] = TaskList.IsDue(t, now),
        ["created"] = t.Created.ToString("o", CultureInfo.InvariantCulture),
    };

    private static string Json(JsonObject o) => o.ToJsonString();

    private static TagDef? FindTag(State s, string name) =>
        s.Tasks.Tags.Items.FirstOrDefault(x => string.Equals(x.Name, name.Trim().TrimStart('#').Trim(), StringComparison.OrdinalIgnoreCase));

    private static JsonArray TagNames(State s) => new(s.Tasks.Tags.Items.Select(x => (JsonNode)JsonValue.Create(x.Name)!).ToArray());

    private static TagDef CreateTag(State s, string name)
    {
        var tag = s.Tasks.Tags.TryAdd(name, out var error) ?? throw new ToolError(error ?? "Could not create the tag.");
        s.TagsChanged = true;
        return tag;
    }

    private static TaskItem Find(JsonObject a, State s)
    {
        var raw = RequireStr(a, "id");
        if (!Guid.TryParse(raw, out var id))
        {
            throw new ToolError($"'{raw}' is not a task id. Use an id from list_tasks.");
        }

        return s.Tasks.Items.FirstOrDefault(i => i.Id == id) ?? throw new ToolError($"No task with id {raw}.");
    }

    private static TaskPriority? OptionalPriority(JsonObject a)
    {
        var raw = OptionalStr(a, "priority");
        if (raw is null)
        {
            return null;
        }

        return TaskPriorities.TryParse(raw, out var p) ? p : throw new ToolError($"'{raw}' is not a priority. Use high, medium, low or none.");
    }

    private static string? OptionalStr(JsonObject a, string key) =>
        a[key] is JsonNode n && n.GetValueKind() == System.Text.Json.JsonValueKind.String ? (string?)n : null;

    private static string RequireStr(JsonObject a, string key) =>
        OptionalStr(a, key) ?? throw new ToolError($"'{key}' is required and must be a string.");

    private static DateTimeOffset? OptionalTime(JsonObject a, string key)
    {
        var raw = OptionalStr(a, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at
            : throw new ToolError($"'{raw}' is not a valid time. Use ISO 8601, e.g. 2026-10-30T09:00:00+05:30.");
    }

    private static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

    private static JsonObject Int(string description) => new() { ["type"] = "integer", ["description"] = description };

    private static JsonObject Bool(string description) => new() { ["type"] = "boolean", ["description"] = description };

    private static SchemaBuilder Schema(params (string Name, JsonObject Def)[] props) => new(props);

    /// <summary>JSON-schema object builder with an optional required list; converts implicitly to <see cref="JsonObject"/>.</summary>
    private sealed class SchemaBuilder
    {
        private readonly JsonObject _obj = new() { ["type"] = "object" };

        public SchemaBuilder((string Name, JsonObject Def)[] props)
        {
            var p = new JsonObject();
            foreach (var (name, def) in props)
            {
                p[name] = def;
            }

            _obj["properties"] = p;
        }

        public SchemaBuilder Require(params string[] names)
        {
            _obj["required"] = new JsonArray(names.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray());
            return this;
        }

        public static implicit operator JsonObject(SchemaBuilder b) => b._obj;
    }
}
