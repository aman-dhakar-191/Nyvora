using System.Text.Json.Nodes;
using Nyvora.Core.Activities;
using Nyvora.Core.Settings;
using Nyvora.Core.Tasks;
using Nyvora.Mcp;
using Xunit;

namespace Nyvora.Tests;

public sealed class ActivityTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nyvora-act-" + Guid.NewGuid().ToString("N"));
    private string Path_ => System.IO.Path.Combine(_dir, ActivityStore.FileName);

    public ActivityTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private string? Up(ActivityUpdate u, DateTimeOffset? at = null) => ActivityStore.Upsert(Path_, u, at ?? Now, out _);

    [Fact]
    public void CreatesAndReadsAnActivity()
    {
        Assert.Null(Up(new ActivityUpdate("deploy", "Deploying v2", Step: 1, Steps: 4, Source: "agent")));
        var a = Assert.Single(ActivityStore.Load(Path_, Now));
        Assert.Equal("Deploying v2", a.Title);
        Assert.Equal(ActivityState.Running, a.State);
        Assert.Equal((1, 4), (a.Step, a.Steps));
    }

    [Fact]
    public void UpdateKeepsFieldsLeftOut()
    {
        Up(new ActivityUpdate("deploy", "Deploying v2", Steps: 4, Detail: "building"));
        Up(new ActivityUpdate("deploy", Step: 2), Now.AddMinutes(1));
        var a = Assert.Single(ActivityStore.Load(Path_, Now.AddMinutes(1)));
        Assert.Equal(("Deploying v2", "building", 2, 4), (a.Title, a.Detail, a.Step, a.Steps));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("a/b")]
    public void RejectsBadIds(string? id) => Assert.NotNull(Up(new ActivityUpdate(id, "x")));

    [Fact]
    public void NewActivityNeedsATitleAndLimitsAreEnforced()
    {
        Assert.NotNull(Up(new ActivityUpdate("a")));
        Assert.NotNull(Up(new ActivityUpdate("a", new string('x', Activity.MaxTitle + 1))));
        Assert.NotNull(Up(new ActivityUpdate("a", "ok", Detail: new string('x', Activity.MaxDetail + 1))));
        Assert.NotNull(Up(new ActivityUpdate("a", "ok", Step: 5, Steps: 4)));
        Assert.NotNull(Up(new ActivityUpdate("a", "ok", State: "nonsense")));
        Assert.Empty(ActivityStore.Load(Path_, Now)); // nothing half-saved
    }

    [Fact]
    public void CapsTheNumberOfActivities()
    {
        for (var i = 0; i < Activity.MaxActivities; i++)
        {
            Assert.Null(Up(new ActivityUpdate("a" + i, "t")));
        }

        Assert.NotNull(Up(new ActivityUpdate("one-too-many", "t")));
        Assert.Null(Up(new ActivityUpdate("a0", "renamed"))); // updating an existing one is still fine
    }

    [Fact]
    public void ActivitiesExpireSoACrashedAgentLeavesNothingBehind()
    {
        Up(new ActivityUpdate("run", "Running"));
        Up(new ActivityUpdate("fin", "Finished", State: "done"));
        Assert.Equal(2, ActivityStore.Load(Path_, Now).Count);
        Assert.Equal(new[] { "run" }, ActivityStore.Load(Path_, Now + Activity.FinishedTtl + TimeSpan.FromSeconds(1)).Select(a => a.Id));
        Assert.Empty(ActivityStore.Load(Path_, Now + Activity.ActiveTtl + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void WaitingNeedsAQuestionAndTheUserCanAnswerOnce()
    {
        Assert.NotNull(Up(new ActivityUpdate("d", "Deploy", State: "waiting_approval"))); // no question
        Assert.Null(Up(new ActivityUpdate("d", "Deploy", State: "waiting_approval", Question: "Ship to prod?")));
        Assert.Null(ActivityStore.Decide(Path_, "d", ApprovalDecision.Approved, Now));
        Assert.Equal(ApprovalDecision.Approved, ActivityStore.Load(Path_, Now).Single().Decision);
        Assert.NotNull(ActivityStore.Decide(Path_, "d", ApprovalDecision.Dismissed, Now)); // already answered
        Assert.NotNull(ActivityStore.Decide(Path_, "missing", ApprovalDecision.Approved, Now));
    }

    [Fact]
    public void AnAnsweredQuestionDisappearsAfterTheShortLifetimeIfTheAgentNeverMovesOn()
    {
        Up(new ActivityUpdate("d", "Deploy", State: "waiting_approval", Question: "Ship?"));
        ActivityStore.Decide(Path_, "d", ApprovalDecision.Approved, Now);
        Assert.Single(ActivityStore.Load(Path_, Now + Activity.FinishedTtl - TimeSpan.FromSeconds(1))); // time to read the answer
        Assert.Empty(ActivityStore.Load(Path_, Now + Activity.FinishedTtl + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AskingAgainStartsAFreshQuestion()
    {
        Up(new ActivityUpdate("d", "Deploy", State: "waiting_approval", Question: "Ship?"));
        ActivityStore.Decide(Path_, "d", ApprovalDecision.Approved, Now);
        Up(new ActivityUpdate("d", State: "running"));
        Up(new ActivityUpdate("d", State: "waiting_approval", Question: "Really ship?"));
        Assert.Equal(ApprovalDecision.None, ActivityStore.Load(Path_, Now).Single().Decision);
    }

    [Fact]
    public void RemoveAndUnreadableFile()
    {
        Up(new ActivityUpdate("a", "t"));
        Assert.True(ActivityStore.Remove(Path_, "a", Now));
        Assert.False(ActivityStore.Remove(Path_, "a", Now));
        File.WriteAllText(Path_, "{ not json");
        Assert.Empty(ActivityStore.Load(Path_, Now));
    }

    // ----- MCP: the master flag -----

    private McpServer Server() => new(new NyvoraTools(_dir, true, false, () => Now), "test");

    private static JsonObject Call(McpServer s, string method, object? p = null) =>
        (JsonObject)JsonNode.Parse(s.Handle(new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method,
            ["params"] = p is null ? null : JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(p)),
        }.ToJsonString())!)!;

    private static string[] Names(McpServer s) =>
        ((JsonArray)Call(s, "tools/list")["result"]!["tools"]!).Select(t => (string)t!["name"]!).ToArray();

    private void SetFlag(bool on) => new AppSettings { ExperimentalFeatures = on }.Save(System.IO.Path.Combine(_dir, "settings.json"));

    [Fact]
    public void FlagOffHidesAndRefusesTheExperimentalTools()
    {
        var s = Server(); // no settings.json at all: the default is off
        Assert.DoesNotContain("publish_activity", Names(s));
        var r = Call(s, "tools/call", new { name = "publish_activity", arguments = new { id = "a", title = "t" } });
        Assert.True((bool)r["result"]!["isError"]!);
        Assert.Empty(ActivityStore.Load(Path_, Now));

        SetFlag(false);
        Assert.DoesNotContain("list_activities", Names(s));
    }

    [Fact]
    public void FlagOnOffsTheToolsWithoutRestartingTheServer()
    {
        var s = Server();
        SetFlag(true);
        Assert.Contains("publish_activity", Names(s));
        var r = Call(s, "tools/call", new { name = "publish_activity", arguments = new { id = "a", title = "Deploying", step = 1, steps = 3 } });
        Assert.False((bool)r["result"]!["isError"]!);
        var list = Call(s, "tools/call", new { name = "list_activities", arguments = new { } });
        Assert.Contains("Deploying", (string)list["result"]!["content"]![0]!["text"]!);

        SetFlag(false);
        Assert.DoesNotContain("publish_activity", Names(s));
        Assert.True((bool)Call(s, "tools/call", new { name = "list_activities", arguments = new { } })["result"]!["isError"]!);
    }

    [Fact]
    public void ReadOnlyServerCannotPublishEvenWithTheFlagOn()
    {
        SetFlag(true);
        var s = new McpServer(new NyvoraTools(_dir, false, false, () => Now), "test");
        Assert.DoesNotContain("publish_activity", Names(s));
        Assert.Contains("list_activities", Names(s));
    }
}

public class ActivityNotchTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static Activity A(string id, string title, ActivityState state = ActivityState.Running, int? step = null, int? steps = null,
        ApprovalDecision decision = ApprovalDecision.None, int minutesAgo = 0) => new()
    {
        Id = id, Title = title, State = state, Step = step, Steps = steps, Decision = decision,
        Question = state == ActivityState.WaitingApproval ? "ok?" : "",
        UpdatedAt = Now.AddMinutes(-minutesAgo), ExpiresAt = Now.AddHours(1),
    };

    [Fact]
    public void NothingMeansNoMessage()
    {
        Assert.Null(ActivityNotch.For(Array.Empty<Activity>(), Now));
        var expired = A("a", "t"); expired.ExpiresAt = Now.AddSeconds(-1);
        Assert.Null(ActivityNotch.For(new[] { expired }, Now));
    }

    [Fact]
    public void OneRunningShowsTitleAndProgress()
    {
        var m = ActivityNotch.For(new[] { A("a", "Deploying", step: 2, steps: 4) }, Now)!;
        Assert.Equal(("Deploying 2/4", NotchTone.Info), (m.Text, m.Tone));
        Assert.Equal("Deploying", ActivityNotch.For(new[] { A("a", "Deploying") }, Now)!.Text);
    }

    [Fact]
    public void SeveralRunningAreCounted()
    {
        Assert.Equal("2 activities running", ActivityNotch.For(new[] { A("a", "One"), A("b", "Two") }, Now)!.Text);
    }

    [Fact]
    public void AQuestionOutranksEverythingAndIsAmber()
    {
        var m = ActivityNotch.For(new[] { A("a", "Build"), A("b", "Deploy", ActivityState.WaitingApproval) }, Now)!;
        Assert.Equal(("Approve: Deploy", NotchTone.Warning), (m.Text, m.Tone));
        var two = ActivityNotch.For(new[] { A("a", "X", ActivityState.WaitingApproval), A("b", "Y", ActivityState.WaitingApproval) }, Now)!;
        Assert.Equal("2 need your approval", two.Text);
    }

    [Fact]
    public void AnAnsweredQuestionCountsAsRunning()
    {
        var m = ActivityNotch.For(new[] { A("a", "Deploy", ActivityState.WaitingApproval, decision: ApprovalDecision.Approved) }, Now)!;
        Assert.Equal(("Deploy", NotchTone.Info), (m.Text, m.Tone));
    }

    [Fact]
    public void FinishedShowsTheMostRecentWithItsTone()
    {
        var done = ActivityNotch.For(new[] { A("a", "Old", ActivityState.Failed, minutesAgo: 5), A("b", "New", ActivityState.Done) }, Now)!;
        Assert.Equal(("New: done", NotchTone.Success), (done.Text, done.Tone));
        var failed = ActivityNotch.For(new[] { A("a", "Build", ActivityState.Failed) }, Now)!;
        Assert.Equal(("Build: failed", NotchTone.Error), (failed.Text, failed.Tone));
    }

    [Fact]
    public void LongTextFitsTheNotch()
    {
        var m = ActivityNotch.For(new[] { A("a", new string('x', 40), step: 10, steps: 12) }, Now)!;
        Assert.Equal(NotchMessage.MaxLength, m.Text.Length);
        Assert.EndsWith("…", m.Text);
    }
}

public sealed class WaitForDecisionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nyvora-wait-" + Guid.NewGuid().ToString("N"));
    private string ActPath => Path.Combine(_dir, ActivityStore.FileName);

    public WaitForDecisionTests()
    {
        Directory.CreateDirectory(_dir);
        new AppSettings { ExperimentalFeatures = true }.Save(Path.Combine(_dir, "settings.json"));
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private McpServer Server(bool write = true) => new(new NyvoraTools(_dir, write, false, () => Now), "test");

    private static JsonObject Call(McpServer s, string name, object args)
    {
        var msg = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call",
            ["params"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new { name, arguments = args })) };
        return (JsonObject)JsonNode.Parse(s.Handle(msg.ToJsonString())!)!;
    }

    private static bool IsError(JsonObject r) => (bool)r["result"]!["isError"]!;

    private static JsonObject Body(JsonObject r) => (JsonObject)JsonNode.Parse((string)r["result"]!["content"]![0]!["text"]!)!;

    private void Ask(string id = "d") =>
        Assert.Null(ActivityStore.Upsert(ActPath, new ActivityUpdate(id, "Deploy", State: "waiting_approval", Question: "Ship?"), Now, out _));

    [Fact]
    public void PublishingAQuestionTellsTheAgentToWait()
    {
        var r = Call(Server(), "publish_activity", new { id = "d", title = "Deploy", state = "waiting_approval", question = "Ship?" });
        Assert.False(IsError(r));
        Assert.Contains("wait_for_decision", (string)Body(r)["next"]!);
        Assert.Null(Body(Call(Server(), "publish_activity", new { id = "e", title = "Build" }))["next"]);
    }

    [Fact]
    public void ReturnsAtOnceWhenAlreadyAnswered()
    {
        Ask();
        Assert.Null(ActivityStore.Decide(ActPath, "d", ApprovalDecision.Approved, Now));
        var b = Body(Call(Server(), "wait_for_decision", new { id = "d", seconds = 5 }));
        Assert.Equal("approved", (string)b["decision"]!);
        Assert.False((bool)b["timedOut"]!);
    }

    [Fact]
    public void TimesOutWithAHintToCallAgain()
    {
        Ask();
        var b = Body(Call(Server(), "wait_for_decision", new { id = "d", seconds = 0 }));
        Assert.Equal("none", (string)b["decision"]!);
        Assert.True((bool)b["timedOut"]!);
        Assert.Contains("again", (string)b["next"]!);
    }

    [Fact]
    public void WaitsAndPicksUpAnAnswerGivenWhileWaiting()
    {
        Ask();
        var answer = Task.Run(async () =>
        {
            await Task.Delay(700);
            ActivityStore.Decide(ActPath, "d", ApprovalDecision.Dismissed, Now);
        });
        var started = System.Diagnostics.Stopwatch.StartNew();
        var b = Body(Call(Server(), "wait_for_decision", new { id = "d", seconds = 10 }));
        answer.Wait();
        Assert.Equal("dismissed", (string)b["decision"]!);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(8)); // it did not sit out the full wait
    }

    [Fact]
    public void UnknownOrNotWaitingActivities()
    {
        Assert.True(IsError(Call(Server(), "wait_for_decision", new { id = "nope", seconds = 0 })));
        Assert.Null(ActivityStore.Upsert(ActPath, new ActivityUpdate("r", "Run"), Now, out _));
        var b = Body(Call(Server(), "wait_for_decision", new { id = "r", seconds = 0 }));
        Assert.False((bool)b["timedOut"]!);
        Assert.Contains("not waiting", (string)b["note"]!);
    }

    [Fact]
    public void IsReadOnlyAndExperimental()
    {
        // A read-only server may wait (it changes nothing)...
        var listed = ((JsonArray)((JsonObject)JsonNode.Parse(Server(write: false).Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""")!)!)["result"]!["tools"]!)
            .Select(t => (string)t!["name"]!);
        Assert.Contains("wait_for_decision", listed);
        Assert.DoesNotContain("publish_activity", listed);
        // ...but with the master flag off the tool is refused.
        new AppSettings { ExperimentalFeatures = false }.Save(Path.Combine(_dir, "settings.json"));
        Assert.True(IsError(Call(Server(), "wait_for_decision", new { id = "d", seconds = 0 })));
    }
}
