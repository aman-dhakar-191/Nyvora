namespace Nyvora.Core.Animation;

public readonly record struct SizeD(double Width, double Height);

public enum PillEase
{
    /// <summary>Fast start, soft landing: opening feels immediate.</summary>
    OutCubic,

    /// <summary>Soft start and landing: closing doesn't snap shut.</summary>
    InOutCubic,
}

/// <summary>
/// Pure size interpolation for one pill animation. Starting from the *current* size makes
/// interruptions continuous: reversing mid-flight never jumps.
/// </summary>
public sealed class PillAnimator
{
    public static readonly SizeD RestingSize = new(148, 36);
    public static readonly SizeD DefaultExpandedSize = new(420, 400);

    /// <summary>The narrow layout (Ctrl+M): small enough to park beside other work.</summary>
    public static readonly SizeD CompactSize = new(340, 300);

    public static readonly TimeSpan ExpandDuration = TimeSpan.FromMilliseconds(320);
    public static readonly TimeSpan SideDuration = TimeSpan.FromMilliseconds(280);

    public static readonly TimeSpan CollapseDuration = TimeSpan.FromMilliseconds(240);

    private readonly SizeD _from;
    private readonly SizeD _to;
    private readonly TimeSpan _duration;
    private readonly PillEase _ease;

    public PillAnimator(SizeD from, SizeD to, TimeSpan duration, PillEase ease = PillEase.OutCubic)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        _from = from;
        _to = to;
        _duration = duration;
        _ease = ease;
    }

    public SizeD Target => _to;

    public bool IsComplete(TimeSpan elapsed) => elapsed >= _duration;

    public SizeD Sample(TimeSpan elapsed)
    {
        var t = Math.Clamp(elapsed / _duration, 0.0, 1.0);
        var e = _ease == PillEase.InOutCubic ? EaseInOutCubic(t) : EaseOutCubic(t);
        return new SizeD(Lerp(_from.Width, _to.Width, e), Lerp(_from.Height, _to.Height, e));
    }

    /// <summary>0 at the resting size, 1 at <paramref name="expanded"/>; drives content cross-fades.</summary>
    public static double ExpansionProgress(SizeD size, SizeD expanded)
    {
        var range = expanded.Width - RestingSize.Width;
        return Math.Clamp((size.Width - RestingSize.Width) / range, 0.0, 1.0);
    }

    public static double EaseInOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    public static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
