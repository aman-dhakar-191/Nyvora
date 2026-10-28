namespace Nyvora.Core.Updates;

public static class AppVersion
{
    /// <summary>Parses "v1.2.3", "1.2" or "1.2.3-beta+abc" into a normalized 4-part version.</summary>
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();
        if (s[0] is 'v' or 'V')
        {
            s = s[1..];
        }

        var cut = s.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
        {
            s = s[..cut];
        }

        if (!Version.TryParse(s, out var parsed))
        {
            return false;
        }

        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
        return true;
    }
}
