using System.Windows;
using System.Windows.Media;

namespace ThumbnailStudio;

public enum ThumbKind
{
    /// <summary>Plain square (list rows, menu-card thumb).</summary>
    Square,
    /// <summary>Board token: a tile with the art inset 6 px and a thin team frame.</summary>
    Token,
    /// <summary>Timeline icon: opaque navy disc, art masked to the circle, team ring.</summary>
    Timeline,
}

/// <summary>Draws a portrait through a Thumb with the game's math (<see cref="Thumb.PortraitRect"/>).</summary>
public sealed class ThumbView : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = Reg(nameof(Source), typeof(ImageSource), null);
    public static readonly DependencyProperty ZoomProperty = Reg(nameof(Zoom), typeof(double), Thumb.Default.Zoom);
    public static readonly DependencyProperty XFracProperty = Reg(nameof(XFrac), typeof(double), Thumb.Default.X);
    public static readonly DependencyProperty YFracProperty = Reg(nameof(YFrac), typeof(double), Thumb.Default.Y);
    public static readonly DependencyProperty KindProperty = Reg(nameof(Kind), typeof(ThumbKind), ThumbKind.Square);
    public static readonly DependencyProperty IsHeroProperty = Reg(nameof(IsHero), typeof(bool), true);
    public static readonly DependencyProperty IsBossProperty = Reg(nameof(IsBoss), typeof(bool), false);

    static DependencyProperty Reg(string name, Type t, object? def) =>
        DependencyProperty.Register(name, t, typeof(ThumbView), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public ImageSource? Source { get => (ImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
    public double XFrac { get => (double)GetValue(XFracProperty); set => SetValue(XFracProperty, value); }
    public double YFrac { get => (double)GetValue(YFracProperty); set => SetValue(YFracProperty, value); }
    public ThumbKind Kind { get => (ThumbKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool IsHero { get => (bool)GetValue(IsHeroProperty); set => SetValue(IsHeroProperty, value); }
    public bool IsBoss { get => (bool)GetValue(IsBossProperty); set => SetValue(IsBossProperty, value); }

    public ThumbView()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        SnapsToDevicePixels = true;
    }

    // Game colours.
    public static readonly Brush HeroFrame = Frozen(Color.FromRgb(0x4D, 0x8B, 0xEB));
    public static readonly Brush EnemyRed = Frozen(Color.FromRgb(0xEB, 0x4D, 0x4D));
    public static readonly Brush HeroGold = Frozen(Color.FromRgb(0xE6, 0xC3, 0x4D));
    public static readonly Brush BossPurple = Frozen(Color.FromRgb(0xDB, 0x66, 0xFF));
    static readonly Brush Navy = Frozen(Color.FromRgb(0x11, 0x12, 0x2B));
    static readonly Brush TileFill = Frozen(Color.FromRgb(0x1E, 0x22, 0x30));
    static readonly Brush TileEdge = Frozen(Color.FromRgb(0x2E, 0x33, 0x45));
    static readonly Brush SquareFill = Frozen(Color.FromRgb(0x14, 0x15, 0x1C));

    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var t = new Thumb(Zoom, XFrac, YFrac);
        var src = Source;

        switch (Kind)
        {
            case ThumbKind.Square:
            {
                double s = Math.Min(w, h);
                var sq = new Rect((w - s) / 2, (h - s) / 2, s, s);
                dc.DrawRectangle(SquareFill, null, sq);
                DrawArt(dc, src, t, sq, new RectangleGeometry(sq));
                break;
            }
            case ThumbKind.Token:
            {
                // A 180 px tile; the art sits in a 168 px square inset 6 px, framed in the team colour.
                double s = Math.Min(w, h), k = s / 180.0, inset = 6 * k;
                var tile = new Rect((w - s) / 2, (h - s) / 2, s, s);
                dc.DrawRoundedRectangle(TileFill, new Pen(TileEdge, 1), tile, 4 * k, 4 * k);
                var art = new Rect(tile.X + inset, tile.Y + inset, s - 2 * inset, s - 2 * inset);
                DrawArt(dc, src, t, art, new RectangleGeometry(art));
                double fw = 2 * k;
                var frame = new Rect(art.X + fw / 2, art.Y + fw / 2, art.Width - fw, art.Height - fw);
                dc.DrawRectangle(null, new Pen(IsHero ? HeroFrame : EnemyRed, fw), frame);
                break;
            }
            case ThumbKind.Timeline:
            {
                // Same build as the battle HUD: opaque disc, sprite box inset 3 px, masked by the circle, 4 px ring.
                double s = Math.Min(w, h), k = s / 88.0, ring = 4 * k, inset = 3 * k;
                var c = new Point(w / 2, h / 2);
                dc.DrawEllipse(Navy, null, c, s / 2, s / 2);
                var box = new Rect(c.X - s / 2 + inset, c.Y - s / 2 + inset, s - 2 * inset, s - 2 * inset);
                DrawArt(dc, src, t, box, new EllipseGeometry(c, s / 2 - ring, s / 2 - ring));
                Brush ringBrush = IsBoss ? BossPurple : IsHero ? HeroGold : EnemyRed;
                dc.DrawEllipse(null, new Pen(ringBrush, ring), c, s / 2 - ring / 2, s / 2 - ring / 2);
                break;
            }
        }
    }

    static void DrawArt(DrawingContext dc, ImageSource? src, Thumb t, Rect square, Geometry clip)
    {
        if (src is not System.Windows.Media.Imaging.BitmapSource bmp) return;
        var r = t.PortraitRect(square.TopLeft, square.Width, bmp.PixelWidth, bmp.PixelHeight);
        dc.PushClip(clip);
        dc.DrawImage(bmp, r);
        dc.Pop();
    }
}
