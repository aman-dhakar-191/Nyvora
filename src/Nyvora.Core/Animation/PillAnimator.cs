namespace Nyvora.Core.Animation;

public readonly record struct SizeD(double Width, double Height);

/// <summary>
/// Pure size interpolation for one pill animation. Starting from the *current* size makes
/// interruptions continuous: reversing mid-flight never jumps.
/// </summary>
public sealed class PillAnimator
{
    public static readonly SizeD RestingSize = new(148, 36);
    public static readonly SizeD ExpandedSize = new(420, 380);

    public static readonly TimeSpan ExpandDuration = TimeSpan.FromMilliseconds(320);
    public static readonly TimeSpan CollapseDuration = TimeSpan.FromMilliseconds(240);

    private readonly SizeD _from;
    private readonly SizeD _to;
    private readonly TimeSpan _duration;

    public PillAnimator(SizeD from, SizeD to, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        _from = from;
        _to = to;
        _duration = duration;
    }

    public SizeD Target => _to;

    public bool IsComplete(TimeSpan elapsed) => elapsed >= _duration;

    public SizeD Sample(TimeSpan elapsed)
    {
        var t = Math.Clamp(elapsed / _duration, 0.0, 1.0);
        var e = EaseOutCubic(t);
        return new SizeD(Lerp(_from.Width, _to.Width, e), Lerp(_from.Height, _to.Height, e));
    }

    /// <summary>0 at the resting size, 1 at the expanded size; drives content cross-fades.</summary>
    public static double ExpansionProgress(SizeD size)
    {
        var range = ExpandedSize.Width - RestingSize.Width;
        return Math.Clamp((size.Width - RestingSize.Width) / range, 0.0, 1.0);
    }

    public static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
