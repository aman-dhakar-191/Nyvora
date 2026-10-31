using System.Text.RegularExpressions;

namespace Nyvora.Core.Tasks;

/// <summary>One thing the add box can offer while a slash command is being typed.</summary>
public sealed record NotchSuggestion(string Insert, string Hint);

/// <summary>What to show under the add box for the current text: clickable suggestions and a one-line hint.</summary>
public sealed record NotchSuggestionSet(string Prefix, IReadOnlyList<NotchSuggestion> Items, string? Hint)
{
    public static readonly NotchSuggestionSet None = new("", Array.Empty<NotchSuggestion>(), null);

    /// <summary>
    /// Moves the highlighted suggestion with the arrow keys: from "none" (-1) Down lands on the first and Up on the last,
    /// and both wrap around. Returns -1 when there is nothing to pick.
    /// </summary>
    public static int Move(int current, int count, int delta)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current < 0 || current >= count)
        {
            return delta > 0 ? 0 : count - 1;
        }

        return (((current + delta) % count) + count) % count;
    }

    /// <summary>The add box's text after picking <paramref name="s"/>: what was typed so far, plus the suggestion and a space.</summary>
    public string Apply(NotchSuggestion s) => Prefix + s.Insert + " ";
}

/// <summary>
/// Suggestions for the "/notch" command, from its pattern: "/notch [timer] [tone:] text". Typing "/no" offers the
/// command; after "/notch " it offers the timers and tones not used yet, narrowed by what has been typed.
/// </summary>
public static class NotchSuggestions
{
    private static readonly NotchSuggestion[] Durations =
    {
        new("10s", "10 seconds"),
        new("30s", "30 seconds"),
        new("15m", "15 minutes"),
        new("30m", "30 minutes"),
        new("1h", "1 hour"),
        new("2h", "2 hours"),
    };

    private static readonly NotchSuggestion[] Tones =
    {
        new("ok:", "green"),
        new("warn:", "amber"),
        new("error:", "red"),
        new("info:", "teal (default)"),
    };

    private static readonly Regex DurationToken = new(@"^\d{1,5}\s*[smh](?=\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ToneToken = new(@"^(info|ok|success|warn|warning|err|error)\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static NotchSuggestionSet For(string? input)
    {
        var text = input ?? "";
        if (!text.StartsWith('/'))
        {
            return NotchSuggestionSet.None;
        }

        var space = text.IndexOfAny(new[] { ' ', '\t' });
        if (space < 0)
        {
            // Still typing the command word itself.
            return "notch".StartsWith(text[1..], StringComparison.OrdinalIgnoreCase)
                ? new NotchSuggestionSet("", new[] { new NotchSuggestion("/notch", "show a message on the notch") }, null)
                : NotchSuggestionSet.None;
        }

        if (!text[..space].Equals("/notch", StringComparison.OrdinalIgnoreCase))
        {
            return NotchSuggestionSet.None;
        }

        // Walk past the options already typed (a timer, a tone), in either order.
        var pos = space;
        var hasTimer = false;
        var hasTone = false;
        for (var i = 0; i < 2; i++)
        {
            while (pos < text.Length && char.IsWhiteSpace(text[pos]))
            {
                pos++;
            }

            var rest = text[pos..];
            if (!hasTimer && DurationToken.Match(rest) is { Success: true } d)
            {
                hasTimer = true;
                pos += d.Length;
            }
            else if (!hasTone && ToneToken.Match(rest) is { Success: true } t)
            {
                hasTone = true;
                pos += t.Length;
            }
            else
            {
                break;
            }
        }

        while (pos < text.Length && char.IsWhiteSpace(text[pos]))
        {
            pos++;
        }

        var partial = text[pos..];
        if (partial.Any(char.IsWhiteSpace))
        {
            return NotchSuggestionSet.None; // the message text itself is being typed
        }

        // Nothing typed after a space that ended the last option: "30m " must not offer "30m" again.
        var prefix = text[..pos];
        var items = new List<NotchSuggestion>();
        if (!hasTimer)
        {
            items.AddRange(Durations.Where(s => s.Insert.StartsWith(partial, StringComparison.OrdinalIgnoreCase)));
        }

        if (!hasTone)
        {
            items.AddRange(Tones.Where(s => s.Insert.StartsWith(partial, StringComparison.OrdinalIgnoreCase)));
        }

        if (partial.Length > 0 && items.Count == 0)
        {
            return NotchSuggestionSet.None; // that is the start of the message, not an option
        }

        var hint = partial.Length > 0 ? null
            : !hasTimer && !hasTone && text[(space + 1)..].Trim().Length == 0 ? "Type a message, or pick a timer or tone first. Enter with no message clears the notch."
            : hasTimer && hasTone ? "Now the message."
            : hasTimer ? "Pick a tone, or just type the message."
            : hasTone ? "Add a timer, or just type the message."
            : null;
        return new NotchSuggestionSet(prefix, items, hint);
    }
}
