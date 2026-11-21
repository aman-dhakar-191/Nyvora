using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Nyvora.Core.Tasks;

namespace Nyvora.Core.Activities;

public enum ActivityState
{
    Running,
    WaitingApproval,
    Done,
    Failed,
}

public enum ApprovalDecision
{
    None,
    Approved,
    Dismissed,
}

/// <summary>
/// Experimental: something an agent is doing for the user, shown on the pill ("Deploying: step 2 of 4"). Unlike a notch
/// message it has a name, steps, a state and can ask the user to approve something. Activities always expire so a
/// crashed agent never leaves one behind.
/// </summary>
public sealed class Activity
{
    public const int MaxActivities = 20;
    public const int MaxId = 40;
    public const int MaxTitle = 40;
    public const int MaxDetail = 200;
    public const int MaxQuestion = 120;
    public const int MaxSource = 30;

    /// <summary>A running or waiting activity not updated for this long is treated as abandoned.</summary>
    public static readonly TimeSpan ActiveTtl = TimeSpan.FromHours(1);

    /// <summary>A finished (done or failed) activity stays visible this long.</summary>
    public static readonly TimeSpan FinishedTtl = TimeSpan.FromMinutes(10);

    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ActivityState State { get; set; }

    public int? Step { get; set; }

    public int? Steps { get; set; }

    public string Detail { get; set; } = "";

    /// <summary>What the agent wants approved; only meaningful while <see cref="State"/> is WaitingApproval.</summary>
    public string Question { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ApprovalDecision Decision { get; set; }

    /// <summary>Which agent or tool published it, as it names itself.</summary>
    public string Source { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public bool IsActive(DateTimeOffset now) => Id.Length > 0 && ExpiresAt > now;

    public bool IsFinished => State is ActivityState.Done or ActivityState.Failed;

    private static readonly Regex IdPattern = new(@"^[A-Za-z0-9._-]{1,40}$", RegexOptions.CultureInvariant);

    public static bool IsValidId(string? id) => id is not null && IdPattern.IsMatch(id);

    /// <summary>Accepts running, waiting (waiting_approval), done, failed, in any case.</summary>
    public static bool TryParseState(string? text, out ActivityState state)
    {
        state = ActivityState.Running;
        switch ((text ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_'))
        {
            case "running":
                return true;
            case "waiting" or "waiting_approval" or "waitingapproval":
                state = ActivityState.WaitingApproval;
                return true;
            case "done" or "success" or "complete" or "completed":
                state = ActivityState.Done;
                return true;
            case "failed" or "error":
                state = ActivityState.Failed;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>The fields an agent may set; anything left null keeps its current value.</summary>
public sealed record ActivityUpdate(
    string? Id,
    string? Title = null,
    string? State = null,
    int? Step = null,
    int? Steps = null,
    string? Detail = null,
    string? Question = null,
    string? Source = null);

/// <summary>activities.json: the current activities, written by the MCP server and the app, under the same cross-process lock style as tasks.json.</summary>
public static class ActivityStore
{
    public const string FileName = "activities.json";

    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);

    /// <summary>The live activities, most recently updated first. Expired ones are left out; a missing or unreadable file is an empty list.</summary>
    public static IReadOnlyList<Activity> Load(string path, DateTimeOffset now) =>
        ReadAll(path).Where(a => a.IsActive(now)).OrderByDescending(a => a.UpdatedAt).ToList();

    /// <summary>Creates or updates one activity. Null on success, otherwise what is wrong (nothing is saved then).</summary>
    public static string? Upsert(string path, ActivityUpdate update, DateTimeOffset now, out Activity? result)
    {
        result = null;
        if (!Activity.IsValidId(update.Id))
        {
            return $"id must be 1 to {Activity.MaxId} letters, digits, dots, dashes or underscores.";
        }

        using var gate = TaskFileLock.Acquire(path, LockWait);
        if (!gate.Acquired)
        {
            return "Nyvora's activity file is busy. Try again in a moment.";
        }

        var all = ReadAll(path).Where(a => a.IsActive(now)).ToList();
        var existing = all.FirstOrDefault(a => a.Id == update.Id);
        var activity = existing is null
            ? new Activity { Id = update.Id! }
            : new Activity
            {
                Id = existing.Id,
                Title = existing.Title,
                State = existing.State,
                Step = existing.Step,
                Steps = existing.Steps,
                Detail = existing.Detail,
                Question = existing.Question,
                Decision = existing.Decision,
                Source = existing.Source,
            };

        if (existing is null && all.Count >= Activity.MaxActivities)
        {
            return $"There are already {Activity.MaxActivities} activities. Clear one first.";
        }

        var error = Apply(activity, update, existing is null);
        if (error is not null)
        {
            return error;
        }

        activity.UpdatedAt = now;
        activity.ExpiresAt = now + (activity.IsFinished ? Activity.FinishedTtl : Activity.ActiveTtl);

        all.RemoveAll(a => a.Id == activity.Id);
        all.Add(activity);
        WriteAll(path, all);
        result = activity;
        return null;
    }

    /// <summary>The user's answer to an approval question. Null on success, otherwise why it could not be recorded.</summary>
    public static string? Decide(string path, string id, ApprovalDecision decision, DateTimeOffset now)
    {
        if (decision == ApprovalDecision.None)
        {
            return "Choose approved or dismissed.";
        }

        using var gate = TaskFileLock.Acquire(path, LockWait);
        if (!gate.Acquired)
        {
            return "Nyvora's activity file is busy. Try again in a moment.";
        }

        var all = ReadAll(path).Where(a => a.IsActive(now)).ToList();
        var activity = all.FirstOrDefault(a => a.Id == id);
        if (activity is null)
        {
            return "That activity is no longer there.";
        }

        if (activity.State != ActivityState.WaitingApproval || activity.Decision != ApprovalDecision.None)
        {
            return "That activity is not waiting for an answer.";
        }

        activity.Decision = decision;
        activity.UpdatedAt = now;
        activity.ExpiresAt = now + Activity.ActiveTtl;
        WriteAll(path, all);
        return null;
    }

    /// <summary>Removes one activity; false if it was not there.</summary>
    public static bool Remove(string path, string id, DateTimeOffset now)
    {
        using var gate = TaskFileLock.Acquire(path, LockWait);
        if (!gate.Acquired)
        {
            return false;
        }

        var all = ReadAll(path).Where(a => a.IsActive(now)).ToList();
        if (all.RemoveAll(a => a.Id == id) == 0)
        {
            return false;
        }

        WriteAll(path, all);
        return true;
    }

    private static string? Apply(Activity a, ActivityUpdate u, bool isNew)
    {
        if (u.Title is not null)
        {
            var t = u.Title.Trim();
            if (t.Length == 0 || t.Length > Activity.MaxTitle)
            {
                return $"title must be 1 to {Activity.MaxTitle} characters.";
            }

            a.Title = t;
        }
        else if (isNew)
        {
            return "title is required when a new activity starts.";
        }

        if (u.Source is not null)
        {
            var s = u.Source.Trim();
            if (s.Length > Activity.MaxSource)
            {
                return $"source can be up to {Activity.MaxSource} characters.";
            }

            a.Source = s;
        }

        if (u.Detail is not null)
        {
            var d = u.Detail.Trim();
            if (d.Length > Activity.MaxDetail)
            {
                return $"detail can be up to {Activity.MaxDetail} characters.";
            }

            a.Detail = d;
        }

        if (u.Question is not null)
        {
            var q = u.Question.Trim();
            if (q.Length > Activity.MaxQuestion)
            {
                return $"question can be up to {Activity.MaxQuestion} characters.";
            }

            a.Question = q;
        }

        if (u.Step is not null)
        {
            a.Step = u.Step;
        }

        if (u.Steps is not null)
        {
            a.Steps = u.Steps;
        }

        if (a.Step is < 0 || a.Steps is < 1 || (a.Step is { } st && a.Steps is { } sts && st > sts))
        {
            return "step must be 0 or more, steps 1 or more, and step cannot be larger than steps.";
        }

        if (u.State is not null)
        {
            if (!Activity.TryParseState(u.State, out var state))
            {
                return "state must be running, waiting_approval, done or failed.";
            }

            if (state == ActivityState.WaitingApproval && a.State != ActivityState.WaitingApproval)
            {
                a.Decision = ApprovalDecision.None; // a new question starts unanswered
            }

            a.State = state;
        }

        if (a.State == ActivityState.WaitingApproval && a.Question.Length == 0)
        {
            return "question is required when the state is waiting_approval: say what should be approved.";
        }

        return null;
    }

    private static List<Activity> ReadAll(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<Activity>>(File.ReadAllText(path)) ?? new List<Activity>()
                : new List<Activity>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<Activity>();
        }
    }

    private static void WriteAll(string path, List<Activity> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }
}
