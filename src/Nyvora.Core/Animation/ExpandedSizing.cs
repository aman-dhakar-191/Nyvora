namespace Nyvora.Core.Animation;

/// <summary>Rules for the user-resizable expanded size (all values in DIPs).</summary>
public static class ExpandedSizing
{
    /// <summary>Smallest size at which the panel's controls still fit without clipping.</summary>
    public static readonly SizeD Min = new(400, 320);

    /// <summary>Forces <paramref name="size"/> between <see cref="Min"/> and what the display can hold.</summary>
    public static SizeD Clamp(SizeD size, SizeD available)
    {
        var maxW = Math.Max(Min.Width, available.Width);
        var maxH = Math.Max(Min.Height, available.Height);
        return new SizeD(
            Math.Clamp(Finite(size.Width), Min.Width, maxW),
            Math.Clamp(Finite(size.Height), Min.Height, maxH));
    }

    /// <summary>Saved size, or the default when nothing valid was saved (0, negative, NaN).</summary>
    public static SizeD FromSettings(double width, double height, SizeD available) =>
        Clamp(width > 0 && height > 0 ? new SizeD(width, height) : PillAnimator.DefaultExpandedSize, available);

    /// <summary>
    /// A drag of the bottom-right grip by (<paramref name="dx"/>, <paramref name="dy"/>). The pill stays centered
    /// under the top edge, so the width changes by twice the horizontal movement to keep the grip under the cursor.
    /// </summary>
    public static SizeD Resize(SizeD start, double dx, double dy, SizeD available) =>
        Clamp(new SizeD(start.Width + 2 * dx, start.Height + dy), available);

    private static double Finite(double v) => double.IsFinite(v) ? v : 0;
}
