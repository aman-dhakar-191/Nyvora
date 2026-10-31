using System.Text.Json.Nodes;
using Nyvora.Core.Settings;
using Nyvora.Core.Tasks;
using Nyvora.Mcp;
using Xunit;

namespace Nyvora.Tests;

public sealed class McpTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nyvora-mcp-" + Guid.NewGuid().ToString("N"));

    public McpTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private McpServer Server(bool write = false, bool delete = false) =>
        new(new NyvoraTools(_dir, write, delete, () => Now), "test");

    private static JsonObject Call(McpServer server, string method, object? @params = null, int id = 1)
    {
        var msg = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (@params is not null)
        {
            msg["params"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(@params));
        }

        var reply = server.Handle(msg.ToJsonString());
        Assert.NotNull(reply);
        Assert.DoesNotContain('\n', reply);
        return (JsonObject)JsonNode.Parse(reply)!;
    }

    private static JsonObject ToolCall(McpServer server, string name, object? args = null) =>
        Call(server, "tools/call", new { name, arguments = args ?? new { } });

    private static string Text(JsonObject reply) => (string)reply["result"]!["content"]![0]!["text"]!;

    private static bool IsError(JsonObject reply) => (bool)reply["result"]!["isError"]!;

    private static JsonObject Payload(JsonObject reply) => (JsonObject)JsonNode.Parse(Text(reply))!;

    private static string[] ToolNames(McpServer s) =>
        ((JsonArray)Call(s, "tools/list")["result"]!["tools"]!).Select(t => (string)t!["name"]!).ToArray();

    [Fact]
    public void InitializeEchoesASupportedVersionAndDeclaresTools()
    {
        var r = Call(Server(), "initialize", new { protocolVersion = "2024-11-05", capabilities = new { }, clientInfo = new { name = "t", version = "1" } });
        Assert.Equal("2.0", (string)r["jsonrpc"]!);
        Assert.Equal("2024-11-05", (string)r["result"]!["protocolVersion"]!);
        Assert.NotNull(r["result"]!["capabilities"]!["tools"]);
        Assert.Equal("nyvora", (string)r["result"]!["serverInfo"]!["name"]!);
    }

    [Fact]
    public void InitializeFallsBackToTheNewestVersionWeSpeak()
    {
        var r = Call(Server(), "initialize", new { protocolVersion = "1999-01-01" });
        Assert.Equal("2025-06-18", (string)r["result"]!["protocolVersion"]!);
    }

    [Fact]
    public void NotificationsGetNoReplyAndPingIsAnswered()
    {
        Assert.Null(Server().Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        Assert.NotNull(Call(Server(), "ping")["result"]);
    }

    [Fact]
    public void BadInputGivesProtocolErrors()
    {
        var s = Server();
        Assert.Equal(-32700, (int)((JsonObject)JsonNode.Parse(s.Handle("{not json")!)!)["error"]!["code"]!);
        Assert.Equal(-32600, (int)((JsonObject)JsonNode.Parse(s.Handle("[1]")!)!)["error"]!["code"]!);
        Assert.Equal(-32601, (int)Call(s, "nope")["error"]!["code"]!);
        Assert.Equal(-32602, (int)ToolCall(s, "no_such_tool")["error"]!["code"]!);
    }

    [Fact]
    public void ReadOnlyByDefaultHidesAndRefusesWriteTools()
    {
        var s = Server();
        var names = ToolNames(s);
        Assert.Contains("list_tasks", names);
        Assert.DoesNotContain("add_task", names);
        Assert.DoesNotContain("delete_task", names);

        var r = ToolCall(s, "add_task", new { title = "x" });
        Assert.True(IsError(r));
        Assert.Contains("--allow-write", Text(r));
        Assert.False(File.Exists(Path.Combine(_dir, "tasks.json")));
    }

    [Fact]
    public void DeleteNeedsItsOwnFlag()
    {
        Assert.DoesNotContain("delete_task", ToolNames(Server(write: true)));
        Assert.Contains("delete_task", ToolNames(Server(delete: true)));
        Assert.Contains("add_task", ToolNames(Server(delete: true)));
        var r = ToolCall(Server(write: true), "delete_task", new { id = Guid.NewGuid().ToString() });
        Assert.True(IsError(r));
        Assert.Contains("--allow-delete", Text(r));
    }

    [Fact]
    public void AddedTaskIsSavedAndListed()
    {
        var s = Server(write: true);
        var added = Payload(ToolCall(s, "add_task", new { title = "  Buy milk  ", remindAt = "2026-10-29T15:00:00+00:00" }));
        Assert.Equal("Buy milk", (string)added["title"]!);
        Assert.Equal("To do", (string)added["status"]!);

        var saved = new JsonTaskStore(Path.Combine(_dir, "tasks.json")).Load();
        Assert.Single(saved);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 15, 0, 0, TimeSpan.Zero), saved[0].RemindAt);

        var listed = Payload(ToolCall(s, "list_tasks"));
        Assert.Equal(1, (int)listed["total"]!);
        Assert.Equal((string)added["id"]!, (string)listed["tasks"]![0]!["id"]!);
    }

    [Fact]
    public void CompleteReopenAndStatusChangesPersist()
    {
        var s = Server(write: true);
        var id = (string)Payload(ToolCall(s, "add_task", new { title = "t" }))["id"]!;

        Assert.True((bool)Payload(ToolCall(s, "complete_task", new { id }))["done"]!);
        Assert.Equal(0, (int)Payload(ToolCall(s, "list_tasks"))["total"]!);
        Assert.Equal(1, (int)Payload(ToolCall(s, "list_tasks", new { filter = "done" }))["total"]!);

        Assert.False((bool)Payload(ToolCall(s, "reopen_task", new { id }))["done"]!);
        var moved = Payload(ToolCall(s, "set_status", new { id, status = "in progress" }));
        Assert.Equal("In progress", (string)moved["status"]!);

        var bad = ToolCall(s, "set_status", new { id, status = "Nope" });
        Assert.True(IsError(bad));
        Assert.Contains("Available:", Text(bad));
    }

    [Fact]
    public void ReminderRenameAndDeleteWork()
    {
        var s = Server(delete: true);
        var id = (string)Payload(ToolCall(s, "add_task", new { title = "old" }))["id"]!;

        var due = Payload(ToolCall(s, "set_reminder", new { id, remindAt = "2026-10-29T11:00:00+00:00" }));
        Assert.True((bool)due["due"]!);
        Assert.Equal(1, (int)Payload(ToolCall(s, "list_tasks", new { filter = "due" }))["total"]!);
        Assert.Equal(1, (int)Payload(ToolCall(s, "list_tasks", new { filter = "today" }))["total"]!);

        Assert.Null(Payload(ToolCall(s, "set_reminder", new { id }))["remindAt"]);
        Assert.Equal("new", (string)Payload(ToolCall(s, "rename_task", new { id, title = "new" }))["title"]!);

        Assert.Equal(id, (string)Payload(ToolCall(s, "delete_task", new { id }))["deleted"]!);
        Assert.Empty(new JsonTaskStore(Path.Combine(_dir, "tasks.json")).Load());
    }

    [Fact]
    public void TagsComeFromSettingsAndCannotBeInvented()
    {
        var settings = new AppSettings { Tags = new List<TagDef> { new() { Id = "t1", Name = "Work", Color = 0 } } };
        settings.Save(Path.Combine(_dir, "settings.json"));
        var s = Server(write: true);
        var id = (string)Payload(ToolCall(s, "add_task", new { title = "t" }))["id"]!;

        Assert.Equal("Work", (string)Payload(ToolCall(s, "list_tags"))["tags"]![0]!);
        var tagged = Payload(ToolCall(s, "tag_task", new { id, tag = "work" }));
        Assert.Equal("Work", (string)tagged["tags"]![0]!);
        Assert.Equal(1, (int)Payload(ToolCall(s, "list_tasks", new { tag = "WORK" }))["total"]!);

        Assert.Empty((JsonArray)Payload(ToolCall(s, "tag_task", new { id, tag = "Work", on = false }))["tags"]!);
        Assert.True(IsError(ToolCall(s, "tag_task", new { id, tag = "Invented" })));
    }

    [Fact]
    public void PriorityCanBeSetFilteredAndShowsInResults()
    {
        var s = Server(write: true);
        var low = (string)Payload(ToolCall(s, "add_task", new { title = "low one", priority = "low" }))["id"]!;
        var plain = Payload(ToolCall(s, "add_task", new { title = "plain" }));
        var high = Payload(ToolCall(s, "add_task", new { title = "high one" }));
        Assert.Equal("None", (string)plain["priority"]!);

        var set = Payload(ToolCall(s, "set_priority", new { id = (string)high["id"]!, priority = "HIGH" }));
        Assert.Equal("High", (string)set["priority"]!);

        var listed = Payload(ToolCall(s, "list_tasks"));
        Assert.Equal("high one", (string)listed["tasks"]![0]!["title"]!); // most important first
        Assert.Equal("low", ((string)Payload(ToolCall(s, "list_tasks", new { priority = "low" }))["tasks"]![0]!["title"]!).Split(' ')[0]);
        Assert.Equal(1, (int)Payload(ToolCall(s, "list_tasks", new { priority = "high" }))["total"]!);

        var saved = new JsonTaskStore(Path.Combine(_dir, "tasks.json")).Load();
        Assert.Equal(TaskPriority.Low, saved.First(x => x.Id.ToString() == low).Priority);
    }

    [Fact]
    public void AgentsCanCreateTagsAndTheyArePersistedInSettings()
    {
        // Other settings must survive the tag being written.
        new AppSettings { AlwaysOnTop = false, StatusReminderMinutes = 15 }.Save(Path.Combine(_dir, "settings.json"));
        var s = Server(write: true);
        var id = (string)Payload(ToolCall(s, "add_task", new { title = "t" }))["id"]!;

        var created = Payload(ToolCall(s, "create_tag", new { name = "#Urgent" }));
        Assert.Equal("Urgent", (string)created["tag"]!);
        Assert.Equal("Urgent", (string)Payload(ToolCall(s, "list_tags"))["tags"]![0]!);

        var saved = AppSettings.Load(Path.Combine(_dir, "settings.json"));
        Assert.Equal("Urgent", saved.Tags.Single().Name);
        Assert.False(saved.AlwaysOnTop);
        Assert.Equal(15, saved.StatusReminderMinutes);

        var tagged = Payload(ToolCall(s, "tag_task", new { id, tag = "urgent" }));
        Assert.Equal("Urgent", (string)tagged["tags"]![0]!);
    }

    [Fact]
    public void CreatingATagThatExistsOrIsInvalidIsAToolError()
    {
        var s = Server(write: true);
        Assert.False(IsError(ToolCall(s, "create_tag", new { name = "Work" })));
        Assert.True(IsError(ToolCall(s, "create_tag", new { name = "work" }))); // same name, ignoring case
        Assert.True(IsError(ToolCall(s, "create_tag", new { name = "" })));
        Assert.True(IsError(ToolCall(s, "create_tag", new { name = new string('x', 21) })));
        Assert.True(IsError(ToolCall(s, "create_tag")));
        Assert.Single(AppSettings.Load(Path.Combine(_dir, "settings.json")).Tags); // failures wrote nothing extra
    }

    [Fact]
    public void TagTaskCanCreateTheTagInOneCall()
    {
        var s = Server(write: true);
        var id = (string)Payload(ToolCall(s, "add_task", new { title = "t" }))["id"]!;

        var refused = ToolCall(s, "tag_task", new { id, tag = "Later" });
        Assert.True(IsError(refused));
        Assert.Contains("create_tag", Text(refused)); // the error says how to fix it
        Assert.Empty(AppSettings.Load(Path.Combine(_dir, "settings.json")).Tags);

        var done = Payload(ToolCall(s, "tag_task", new { id, tag = "Later", create = true }));
        Assert.Equal("Later", (string)done["tags"]![0]!);
        Assert.Equal("Later", AppSettings.Load(Path.Combine(_dir, "settings.json")).Tags.Single().Name);
    }

    [Fact]
    public void TheTagLimitIsEnforcedForAgents()
    {
        var s = Server(write: true);
        for (var i = 0; i < TagSet.MaxTags; i++)
        {
            Assert.False(IsError(ToolCall(s, "create_tag", new { name = "t" + i })));
        }

        Assert.True(IsError(ToolCall(s, "create_tag", new { name = "one-too-many" })));
        Assert.Equal(TagSet.MaxTags, AppSettings.Load(Path.Combine(_dir, "settings.json")).Tags.Count);
    }

    [Fact]
    public void ReadOnlyServersCannotCreateTags()
    {
        Assert.DoesNotContain("create_tag", ToolNames(Server()));
        Assert.True(IsError(ToolCall(Server(), "create_tag", new { name = "x" })));
    }

    [Fact]
    public void NotchMessagesCanBeSetReadAndCleared()
    {
        var s = Server(write: true);
        Assert.Null(Payload(ToolCall(s, "get_notch_message"))["message"]);

        var set = Payload(ToolCall(s, "set_notch_message", new { text = "Deploying v2", tone = "warn", minutes = 30 }));
        Assert.Equal("Deploying v2", (string)set["message"]!);
        Assert.Equal("warning", (string)set["tone"]!);

        var shown = NotchMessageStore.Load(Path.Combine(_dir, "notch.json"), Now);
        Assert.Equal("Deploying v2", shown!.Text); // this is the file the app watches
        Assert.Equal(Now.AddMinutes(30), shown.ExpiresAt);
        Assert.Equal("Deploying v2", (string)Payload(ToolCall(s, "get_notch_message"))["message"]!);

        ToolCall(s, "clear_notch_message");
        Assert.Null(Payload(ToolCall(s, "get_notch_message"))["message"]);
        Assert.Null(NotchMessageStore.Load(Path.Combine(_dir, "notch.json"), Now));
    }

    [Fact]
    public void BadNotchMessagesAreToolErrors()
    {
        var s = Server(write: true);
        Assert.True(IsError(ToolCall(s, "set_notch_message", new { text = "" })));
        Assert.True(IsError(ToolCall(s, "set_notch_message", new { text = new string('x', 41) })));
        Assert.True(IsError(ToolCall(s, "set_notch_message", new { text = "hi", tone = "purple" })));
        Assert.True(IsError(ToolCall(s, "set_notch_message", new { text = "hi", minutes = 0 })));
        Assert.True(IsError(ToolCall(s, "set_notch_message")));
    }

    [Fact]
    public void ReadOnlyServersCanReadButNotChangeTheNotch()
    {
        var names = ToolNames(Server());
        Assert.Contains("get_notch_message", names);
        Assert.DoesNotContain("set_notch_message", names);
        Assert.DoesNotContain("clear_notch_message", names);
        Assert.True(IsError(ToolCall(Server(), "set_notch_message", new { text = "hi" })));
    }

    [Fact]
    public void BadPriorityIsAToolError()
    {
        var s = Server(write: true);
        var id = (string)Payload(ToolCall(s, "add_task", new { title = "t" }))["id"]!;
        Assert.True(IsError(ToolCall(s, "add_task", new { title = "x", priority = "urgent" })));
        Assert.True(IsError(ToolCall(s, "set_priority", new { id, priority = "urgent" })));
        Assert.True(IsError(ToolCall(s, "set_priority", new { id })));
        Assert.DoesNotContain("set_priority", ToolNames(Server()));
    }

    [Fact]
    public void BadArgumentsAreToolErrorsNotCrashes()
    {
        var s = Server(write: true);
        Assert.True(IsError(ToolCall(s, "add_task", new { title = "" })));
        Assert.True(IsError(ToolCall(s, "add_task")));
        Assert.True(IsError(ToolCall(s, "add_task", new { title = "x", remindAt = "soonish" })));
        Assert.True(IsError(ToolCall(s, "complete_task", new { id = "not-a-guid" })));
        Assert.True(IsError(ToolCall(s, "complete_task", new { id = Guid.NewGuid().ToString() })));
        Assert.True(IsError(ToolCall(s, "list_tasks", new { filter = "weird" })));
    }

    [Fact]
    public void ListIsOrderedInProgressFirstAndRespectsLimit()
    {
        var s = Server(write: true);
        ToolCall(s, "add_task", new { title = "todo" });
        var doing = (string)Payload(ToolCall(s, "add_task", new { title = "doing" }))["id"]!;
        ToolCall(s, "set_status", new { id = doing, status = "In progress" });

        var all = Payload(ToolCall(s, "list_tasks"));
        Assert.Equal("doing", (string)all["tasks"]![0]!["title"]!);
        var one = Payload(ToolCall(s, "list_tasks", new { limit = 1 }));
        Assert.Equal(2, (int)one["total"]!);
        Assert.Single((JsonArray)one["tasks"]!);
    }

    [Fact]
    public void RunReadsLinesAndWritesOneReplyPerRequest()
    {
        var input = new StringReader(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""" + "\n" +
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""" + "\n\n" +
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""" + "\n");
        var output = new StringWriter { NewLine = "\n" };
        Server().Run(input, output);

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal(1, (int)JsonNode.Parse(lines[0])!["id"]!);
        Assert.Equal(2, (int)JsonNode.Parse(lines[1])!["id"]!);
    }

    [Fact]
    public void ReplaceItemsRaisesChangedAndNormalizes()
    {
        var l = new TaskList();
        l.Add("a", Now);
        var raised = 0;
        l.Changed += () => raised++;

        l.ReplaceItems(new[] { new TaskItem { Id = Guid.NewGuid(), Title = "b", Created = Now } });

        Assert.Equal(1, raised);
        Assert.Equal("b", Assert.Single(l.Items).Title);
        Assert.NotNull(l.Items[0].StatusId);
    }

    [Fact]
    public void SnapshotComparisonSeesRealChangesOnly()
    {
        var a = new List<TaskItem> { new() { Id = Guid.NewGuid(), Title = "x", Created = Now } };
        var b = new List<TaskItem> { new() { Id = a[0].Id, Title = "x", Created = Now } };
        Assert.True(TaskSnapshot.Same(a, b));
        b[0].Title = "y";
        Assert.False(TaskSnapshot.Same(a, b));
    }

    [Fact]
    public void FileLockIsExclusiveBetweenHolders()
    {
        var path = Path.Combine(_dir, "tasks.json");
        using var first = TaskFileLock.Acquire(path, TimeSpan.FromSeconds(1));
        Assert.True(first.Acquired);

        // A second holder must be a genuinely different thread: Task.Run(...).Result can run the work inline on this
        // thread, which would simply re-enter our own lock.
        var got = true;
        var other = new Thread(() =>
        {
            using var second = TaskFileLock.Acquire(path, TimeSpan.FromMilliseconds(200));
            got = second.Acquired;
        });
        other.Start();
        other.Join();
        Assert.False(got);
    }
}
