using Nyvora.Core.Tasks;

namespace Nyvora.Core.Rules;

/// <summary>What a rule looks at. Only data Nyvora already has: no sensors, no network.</summary>
public sealed record NudgeContext(DateTimeOffset Now, IReadOnlyList<TaskItem> Tasks);

/// <summary>A nudge and the rule that produced it, so the user can always see why it appeared.</summary>
public sealed record Nudge(string RuleId, string RuleName, string Message, NotchTone Tone);

/// <summary>One deterministic rule: looks at the context, and either has something to say or does not.</summary>
public interface INudgeRule
{
    string Id { get; }

    string Name { get; }

    /// <summary>One line for Settings: what this rule does.</summary>
    string Description { get; }

    /// <summary>After it fires, it stays quiet at least this long.</summary>
    TimeSpan Cooldown { get; }

    Nudge? Evaluate(NudgeContext context);
}

/// <summary>
/// Decides, from rules alone, whether the pill may speak up now. Pure: time is passed in, no model, no I/O, so every
/// decision can be tested. Nothing fires right after start-up, outside the active hours, or too close to the last nudge,
/// and a rule the user muted never runs. State is in memory only. Not thread-safe: use it from the UI thread.
/// </summary>
public sealed class RuleEngine
{
    public static readonly TimeSpan DefaultStartupGrace = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DefaultMinGap = TimeSpan.FromMinutes(45);
    public const int DefaultMaxPerDay = 4;
    public static readonly TimeSpan DefaultActiveFrom = TimeSpan.FromHours(8);
    public static readonly TimeSpan DefaultActiveTo = TimeSpan.FromHours(20);

    private readonly IReadOnlyList<INudgeRule> _rules;
    private readonly DateTimeOffset _startedAt;
    private readonly Dictionary<string, DateTimeOffset> _lastFired = new();
    private DateTimeOffset? _lastAny;
    private DateOnly _day;
    private int _firedToday;

    public RuleEngine(IEnumerable<INudgeRule> rules, DateTimeOffset startedAt)
    {
        _rules = rules.ToList();
        _startedAt = startedAt;
    }

    public TimeSpan StartupGrace { get; init; } = DefaultStartupGrace;

    public TimeSpan MinGap { get; init; } = DefaultMinGap;

    public int MaxPerDay { get; init; } = DefaultMaxPerDay;

    public TimeSpan ActiveFrom { get; init; } = DefaultActiveFrom;

    public TimeSpan ActiveTo { get; init; } = DefaultActiveTo;

    public IReadOnlyList<INudgeRule> Rules => _rules;

    /// <summary>The first nudge that may fire now, or null. A nudge that is returned counts as fired.</summary>
    public Nudge? Evaluate(NudgeContext context, ISet<string> muted)
    {
        var now = context.Now;
        if (now - _startedAt < StartupGrace)
        {
            return null;
        }

        var local = now.TimeOfDay;
        if (local < ActiveFrom || local >= ActiveTo)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(now.DateTime);
        if (today != _day)
        {
            _day = today;
            _firedToday = 0;
        }

        if (_firedToday >= MaxPerDay || (_lastAny is { } last && now - last < MinGap))
        {
            return null;
        }

        foreach (var rule in _rules)
        {
            if (muted.Contains(rule.Id))
            {
                continue;
            }

            if (_lastFired.TryGetValue(rule.Id, out var at) && now - at < rule.Cooldown)
            {
                continue;
            }

            var nudge = rule.Evaluate(context);
            if (nudge is null)
            {
                continue;
            }

            _lastFired[rule.Id] = now;
            _lastAny = now;
            _firedToday++;
            return nudge;
        }

        return null;
    }
}
