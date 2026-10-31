using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyvora.Core.Tasks;

public enum NotchTone
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// A short message shown on the resting pill in place of the counters, set by the user (the add box: "/notch ...")
/// or by an AI agent (the MCP server). It can expire on its own.
/// </summary>
public sealed class NotchMessage
{
    public const int MaxLength = 40;
    public const int MaxMinutes = 24 * 60;

    public string Text { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NotchTone Tone { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public bool IsActive(DateTimeOffset now) => Text.Length > 0 && (ExpiresAt is null || ExpiresAt > now);

    /// <summary>Builds a message; null on success, otherwise what is wrong with the input.</summary>
    public static string? TryCreate(string? text, string? tone, int? minutes, DateTimeOffset now, out NotchMessage? message)
    {
        message = null;
        var t = (text ?? "").Trim();
        if (t.Length == 0)
        {
            return "The message is empty.";
        }

        if (t.Length > MaxLength)
        {
            return $"The message is {t.Length} characters; the notch fits {MaxLength}.";
        }

        if (!TryParseTone(tone, out var parsedTone))
        {
            return "tone must be info, success, warning or error.";
        }

        if (minutes is { } m && (m < 1 || m > MaxMinutes))
        {
            return $"minutes must be between 1 and {MaxMinutes}.";
        }

        message = new NotchMessage
        {
            Text = t,
            Tone = parsedTone,
            ExpiresAt = minutes is { } mm ? now.AddMinutes(mm) : null,
        };
        return null;
    }

    /// <summary>Accepts info, success (ok), warning (warn), error, in any case; blank means info.</summary>
    public static bool TryParseTone(string? text, out NotchTone tone)
    {
        tone = NotchTone.Info;
        switch ((text ?? "").Trim().ToLowerInvariant())
        {
            case "" or "info":
                return true;
            case "success" or "ok":
                tone = NotchTone.Success;
                return true;
            case "warning" or "warn":
                tone = NotchTone.Warning;
                return true;
            case "error" or "err":
                tone = NotchTone.Error;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The add box's command: "/notch text" sets a message, "/notch warn: text" sets it with a tone, and a bare
    /// "/notch" clears it. Returns false when the input is not a notch command at all.
    /// </summary>
    public static bool TryParseCommand(string? input, out string payload)
    {
        payload = "";
        var s = (input ?? "").Trim();
        const string cmd = "/notch";
        if (!s.StartsWith(cmd, StringComparison.OrdinalIgnoreCase) || (s.Length > cmd.Length && !char.IsWhiteSpace(s[cmd.Length])))
        {
            return false;
        }

        payload = s[cmd.Length..].Trim();
        return true;
    }

    private static readonly System.Text.RegularExpressions.Regex DurationToken =
        new(@"^(\d{1,4})\s*([mh])(?=\s|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex ToneToken =
        new(@"^(info|ok|success|warn|warning|err|error)\s*:\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads what follows "/notch": up to two leading options, in either order, then the text.
    /// <c>30m</c> or <c>2h</c> is how long the message stays; <c>warn:</c> (or ok:, error:, info:) is its tone.
    /// "30m warn: Server down", "warn: 30m Server down" and "Server down" all work.
    /// </summary>
    public static (string? Tone, int? Minutes, string Text) ParseCommandText(string payload)
    {
        string? tone = null;
        int? minutes = null;
        var rest = payload.Trim();
        for (var i = 0; i < 2; i++)
        {
            if (minutes is null && DurationToken.Match(rest) is { Success: true } d)
            {
                var n = int.Parse(d.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                minutes = d.Groups[2].Value.Equals("h", StringComparison.OrdinalIgnoreCase) ? n * 60 : n;
                rest = rest[d.Length..].TrimStart();
            }
            else if (tone is null && ToneToken.Match(rest) is { Success: true } t)
            {
                tone = t.Groups[1].Value;
                rest = rest[t.Length..].TrimStart();
            }
        }

        return (tone, minutes, rest);
    }

    /// <summary>Splits an optional leading "tone:" off command text: "warn: server down" becomes (warn, server down).</summary>
    public static (string? Tone, string Text) SplitTone(string payload)
    {
        var i = payload.IndexOf(':');
        if (i > 0 && i <= 8 && TryParseTone(payload[..i], out _) && payload[..i].Trim().Length > 0)
        {
            return (payload[..i].Trim(), payload[(i + 1)..].Trim());
        }

        return (null, payload);
    }
}

/// <summary>notch.json: one message, written by the app or the MCP server and read by the app.</summary>
public static class NotchMessageStore
{
    public const string FileName = "notch.json";

    /// <summary>The current message, or null when there is none, it has expired, or the file is unreadable.</summary>
    public static NotchMessage? Load(string path, DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var m = JsonSerializer.Deserialize<NotchMessage>(File.ReadAllText(path));
            return m is not null && m.IsActive(now) ? m : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string path, NotchMessage message) => Write(path, message);

    /// <summary>Clearing writes an empty message rather than deleting, so file watchers see an ordinary change.</summary>
    public static void Clear(string path) => Write(path, new NotchMessage());

    private static void Write(string path, NotchMessage message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(message, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }
}
