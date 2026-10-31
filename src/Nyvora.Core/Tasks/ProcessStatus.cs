namespace Nyvora.Core.Tasks;

/// <summary>
/// What Nyvora itself shows on the notch about its own work (an update downloading or installing, a sync). These
/// are separate from messages a person or agent sets: they come and go by themselves and are never written to disk.
/// </summary>
public static class ProcessStatus
{
    public static readonly TimeSpan FailedFor = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan SyncedFor = TimeSpan.FromSeconds(5);

    /// <summary>
    /// An update in progress: "Downloading v1.9.1 · 42%" while the amount is known, otherwise the step
    /// (for example "Verifying download…"), otherwise "Updating v1.9.1…". Shown until the update changes state.
    /// </summary>
    public static NotchMessage Updating(string version, double? progress, string? step)
    {
        string text;
        if (progress is { } p && double.IsFinite(p))
        {
            text = $"Downloading {version} · {(int)Math.Round(Math.Clamp(p, 0, 1) * 100)}%";
        }
        else if (!string.IsNullOrWhiteSpace(step))
        {
            text = step.Trim();
        }
        else
        {
            text = $"Updating {version}…";
        }

        return Make(text, NotchTone.Info, null);
    }

    public static NotchMessage UpdateFailed(DateTimeOffset now) => Make("Update failed", NotchTone.Error, now + FailedFor);

    /// <summary>tasks.json was changed by another program (an agent, say) and the app took the change in.</summary>
    public static NotchMessage Synced(DateTimeOffset now) => Make("Synced from another app", NotchTone.Success, now + SyncedFor);

    private static NotchMessage Make(string text, NotchTone tone, DateTimeOffset? expires)
    {
        if (text.Length > NotchMessage.MaxLength)
        {
            text = text[..(NotchMessage.MaxLength - 1)].TrimEnd() + "…";
        }

        return new NotchMessage { Text = text, Tone = tone, ExpiresAt = expires };
    }
}
