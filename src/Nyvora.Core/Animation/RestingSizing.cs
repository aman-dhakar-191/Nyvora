namespace Nyvora.Core.Animation;

/// <summary>How wide the resting pill is: its content plus padding, never narrower than the default, never absurdly wide.</summary>
public static class RestingSizing
{
    public const double Padding = 20;
    public const double MaxWidth = 360;

    public static double Width(double contentWidth, double availableWidth)
    {
        var wanted = (double.IsFinite(contentWidth) ? contentWidth : 0) + 2 * Padding;
        var max = Math.Max(PillAnimator.RestingSize.Width, Math.Min(MaxWidth, availableWidth));
        return Math.Clamp(wanted, PillAnimator.RestingSize.Width, max);
    }
}
