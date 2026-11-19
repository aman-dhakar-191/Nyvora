using Nyvora.Core.Tasks;

namespace Nyvora.Core.Timesheet;

/// <summary>What the Keka fill script reports (its exit code) and what the notch says about it.</summary>
public enum KekaOutcome
{
    Filled = 0,
    SignInNeeded = 10,
    PageChanged = 11,
    PythonMissing = 12,
    Failed = 13,
    PlaywrightMissing = 14,
    ProfileBusy = 15,
}

public static class KekaResult
{
    public static readonly TimeSpan SuccessFor = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ProblemFor = TimeSpan.FromHours(1);

    public static KekaOutcome FromExitCode(int code) =>
        Enum.IsDefined(typeof(KekaOutcome), code) ? (KekaOutcome)code : KekaOutcome.Failed;

    public static NotchMessage Running() =>
        new() { Text = "Keka: filling timesheet…", Tone = NotchTone.Info };

    public static NotchMessage Notch(KekaOutcome outcome, DateTimeOffset now) => outcome switch
    {
        KekaOutcome.Filled => new NotchMessage { Text = "Keka filled: review and submit", Tone = NotchTone.Success, ExpiresAt = now + SuccessFor },
        KekaOutcome.SignInNeeded => new NotchMessage { Text = "Keka: sign in needed", Tone = NotchTone.Error, ExpiresAt = now + ProblemFor },
        KekaOutcome.PageChanged => new NotchMessage { Text = "Keka: page changed, see log", Tone = NotchTone.Error, ExpiresAt = now + ProblemFor },
        KekaOutcome.PythonMissing => new NotchMessage { Text = "Keka: Python not found", Tone = NotchTone.Error, ExpiresAt = now + ProblemFor },
        KekaOutcome.PlaywrightMissing => new NotchMessage { Text = "Keka: setup failed, see log", Tone = NotchTone.Error, ExpiresAt = now + ProblemFor },
        KekaOutcome.ProfileBusy => new NotchMessage { Text = "Keka: close its open Edge window", Tone = NotchTone.Error, ExpiresAt = now + ProblemFor },
        _ => new NotchMessage { Text = "Keka: could not fill, see log", Tone = NotchTone.Error, ExpiresAt = now + ProblemFor },
    };

    /// <summary>The timesheet address must be a web address over https (Keka is always https). Null when fine, else why not.</summary>
    public static string? ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "Enter your Keka timesheet address (for example https://yourcompany.keka.com/...).";
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Contains('.')
            ? null
            : "The address must start with https:// and be a full web address.";
    }
}
