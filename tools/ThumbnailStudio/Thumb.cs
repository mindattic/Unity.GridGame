using System.Globalization;
using System.Windows;

namespace ThumbnailStudio;

/// <summary>
/// One class's thumbnail framing, exactly as the game stores it in classes.json ("Thumbnail").
/// X, Y: the portrait point (fractions of the portrait's width / height, top-left origin) that sits at the
/// thumbnail's centre. Zoom: portrait width / thumbnail side (1 = the whole portrait width fills the square).
/// </summary>
public readonly record struct Thumb(double Zoom, double X, double Y)
{
    public const double MinZoom = 0.25, MaxZoom = 20, MinXY = -0.5, MaxXY = 1.5;

    /// <summary>Used until a class has a saved Thumbnail.</summary>
    public static readonly Thumb Default = new(1.6, 0.5, 0.3);

    public Thumb Clamped() => new(
        Math.Clamp(Zoom, MinZoom, MaxZoom),
        Math.Clamp(X, MinXY, MaxXY),
        Math.Clamp(Y, MinXY, MaxXY));

    /// <summary>Rounded to the 4 decimals that are written to disk, so "dirty" compares what would be saved.</summary>
    public Thumb Rounded() => new(Math.Round(Zoom, 4), Math.Round(X, 4), Math.Round(Y, 4));

    public static string Num(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);

    public override string ToString() => $"Zoom {Num(Zoom)}  X {Num(X)}  Y {Num(Y)}";

    /// <summary>
    /// The game's rendering math: in a square of side <paramref name="side"/> whose top-left is
    /// <paramref name="origin"/>, the portrait is drawn at W = Zoom*S, H = W*(imgH/imgW),
    /// left = S/2 - X*W, top = S/2 - Y*H (then clipped to the square).
    /// </summary>
    public Rect PortraitRect(Point origin, double side, double imgW, double imgH)
    {
        double w = Zoom * side, h = imgW > 0 ? w * (imgH / imgW) : w;
        return new Rect(origin.X + side / 2 - X * w, origin.Y + side / 2 - Y * h, w, h);
    }
}
