using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ThumbnailStudio;

/// <summary>
/// The framing canvas. The target square stays fixed in the middle and is the thumbnail, drawn large; the real
/// portrait is placed under it with the game's math, so what is inside the square is exactly what the game shows.
/// Wheel zooms about the cursor, left-drag moves the portrait under the square, and the overview (bottom-left)
/// shows the whole portrait with the cut-out marked; click or drag there to aim the square directly.
/// </summary>
public sealed class EditorSurface : FrameworkElement
{
    BitmapSource? image;
    Thumb thumb = Thumb.Default;

    /// <summary>Raised whenever the user changes the framing here.</summary>
    public event Action<Thumb>? ThumbEdited;

    public BitmapSource? Image { get => image; set { image = value; InvalidateVisual(); } }
    public Thumb Thumb { get => thumb; set { thumb = value; InvalidateVisual(); } }
    public string? EmptyText { get; set; }

    static readonly Brush Back = Frozen(Color.FromRgb(0x0F, 0x10, 0x15));
    static readonly Brush Dim = Frozen(Color.FromArgb(0xB4, 0x08, 0x09, 0x0D));
    static readonly Brush Accent = Frozen(Color.FromRgb(0xE6, 0xC3, 0x4D));
    static readonly Brush Guide = Frozen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
    static readonly Brush OverviewBack = Frozen(Color.FromArgb(0xE0, 0x16, 0x17, 0x1F));
    static readonly Brush OverviewEdge = Frozen(Color.FromRgb(0x3A, 0x3D, 0x4C));
    static readonly Brush TextBrush = Frozen(Color.FromRgb(0x9A, 0x9C, 0xAA));
    static readonly Brush Checker = MakeChecker();

    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    static Brush MakeChecker()
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Frozen(Color.FromRgb(0x2B, 0x2C, 0x34)), null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
        var dark = new GeometryGroup();
        dark.Children.Add(new RectangleGeometry(new Rect(0, 0, 1, 1)));
        dark.Children.Add(new RectangleGeometry(new Rect(1, 1, 1, 1)));
        g.Children.Add(new GeometryDrawing(Frozen(Color.FromRgb(0x21, 0x22, 0x29)), null, dark));
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 24, 24), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        b.Freeze();
        return b;
    }

    public EditorSurface()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Cursor = Cursors.SizeAll;
    }

    /// <summary>The fixed target square: 64% of the shorter side, centred.</summary>
    Rect Square
    {
        get
        {
            double s = Math.Max(40, Math.Min(ActualWidth, ActualHeight) * 0.64);
            return new Rect((ActualWidth - s) / 2, (ActualHeight - s) / 2, s, s);
        }
    }

    Rect PortraitRect(Thumb t)
    {
        var sq = Square;
        return image == null ? Rect.Empty : t.PortraitRect(sq.TopLeft, sq.Width, image.PixelWidth, image.PixelHeight);
    }

    /// <summary>Overview box bottom-left: the whole portrait fitted to 170 px tall.</summary>
    Rect OverviewRect
    {
        get
        {
            if (image == null) return Rect.Empty;
            double h = Math.Min(170, ActualHeight * 0.3), w = h * image.PixelWidth / image.PixelHeight;
            return new Rect(14, ActualHeight - h - 14, w, h);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var all = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(Back, null, all);
        if (image == null)
        {
            DrawText(dc, EmptyText ?? "No portrait", new Point(ActualWidth / 2, ActualHeight / 2), 15, center: true);
            return;
        }
        var sq = Square;
        var pr = PortraitRect(thumb);

        // Checkerboard where the portrait is (it has alpha), then the portrait itself.
        var ci = Rect.Intersect(pr, all);
        if (!ci.IsEmpty) dc.DrawRectangle(Checker, null, ci);
        dc.DrawImage(image, pr);

        // Dim everything outside the target square.
        dc.DrawGeometry(Dim, null, new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(all), new RectangleGeometry(sq)));

        // Thirds guides, the timeline circle and the square frame.
        var gp = new Pen(Guide, 1) { DashStyle = DashStyles.Dash };
        for (int i = 1; i < 3; i++)
        {
            dc.DrawLine(gp, new Point(sq.X + sq.Width * i / 3, sq.Y), new Point(sq.X + sq.Width * i / 3, sq.Bottom));
            dc.DrawLine(gp, new Point(sq.X, sq.Y + sq.Height * i / 3), new Point(sq.Right, sq.Y + sq.Height * i / 3));
        }
        var c = new Point(sq.X + sq.Width / 2, sq.Y + sq.Height / 2);
        // The timeline icon's mask: an 88 px icon draws the art in an 82 px box masked by a radius-40 circle.
        double tr = sq.Width * 40.0 / 82.0;
        dc.DrawEllipse(null, new Pen(Guide, 1.5), c, tr, tr);
        dc.DrawLine(new Pen(Guide, 1), new Point(c.X - 8, c.Y), new Point(c.X + 8, c.Y));
        dc.DrawLine(new Pen(Guide, 1), new Point(c.X, c.Y - 8), new Point(c.X, c.Y + 8));
        dc.DrawRectangle(null, new Pen(Accent, 2), sq);
        DrawText(dc, "THUMBNAIL", new Point(sq.X, sq.Y - 20), 11, center: false, brush: Accent);

        // Overview: the whole portrait with the cut-out marked.
        var ov = OverviewRect;
        dc.DrawRectangle(OverviewBack, new Pen(OverviewEdge, 1), Rect.Inflate(ov, 6, 6));
        dc.DrawRectangle(Checker, null, ov);
        dc.DrawImage(image, ov);
        dc.PushClip(new RectangleGeometry(Rect.Inflate(ov, 6, 6)));
        double fw = ov.Width / thumb.Zoom, fh = fw; // the square's side, in overview pixels
        var cut = new Rect(ov.X + thumb.X * ov.Width - fw / 2, ov.Y + thumb.Y * ov.Height - fh / 2, fw, fh);
        dc.DrawRectangle(null, new Pen(Accent, 1.5), cut);
        dc.Pop();
        DrawText(dc, "OVERVIEW · click to aim", new Point(ov.X - 4, ov.Y - 24), 10, center: false);
    }

    void DrawText(DrawingContext dc, string s, Point p, double size, bool center, Brush? brush = null)
    {
        var ft = new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), size, brush ?? TextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, center ? new Point(p.X - ft.Width / 2, p.Y - ft.Height / 2) : p);
    }

    void Edit(Thumb t)
    {
        t = t.Clamped();
        if (t == thumb) return;
        thumb = t;
        InvalidateVisual();
        ThumbEdited?.Invoke(t);
    }

    // ---- Operations (also used by the keyboard and the window's buttons) ----

    /// <summary>Zooms by <paramref name="factor"/>, keeping the portrait point under <paramref name="anchor"/> still.</summary>
    public void ZoomAbout(Point anchor, double factor)
    {
        if (image == null) return;
        var sq = Square;
        var pr = PortraitRect(thumb);
        double px = (anchor.X - pr.X) / pr.Width, py = (anchor.Y - pr.Y) / pr.Height;
        double z = Math.Clamp(thumb.Zoom * factor, Thumb.MinZoom, Thumb.MaxZoom);
        double w = z * sq.Width, h = w * image.PixelHeight / image.PixelWidth;
        double left = anchor.X - px * w, top = anchor.Y - py * h;
        Edit(new Thumb(z, (sq.X + sq.Width / 2 - left) / w, (sq.Y + sq.Height / 2 - top) / h));
    }

    /// <summary>Centre of the target square, in this control's coordinates.</summary>
    public Point SquareCentre { get { var sq = Square; return new Point(sq.X + sq.Width / 2, sq.Y + sq.Height / 2); } }

    public double SquareSide => Square.Width;

    /// <summary>Where portrait point (fx, fy) (fractions of the portrait) is on screen right now.</summary>
    public Point ScreenOf(double fx, double fy)
    {
        var pr = PortraitRect(thumb);
        return pr.IsEmpty ? SquareCentre : new Point(pr.X + fx * pr.Width, pr.Y + fy * pr.Height);
    }

    public void ZoomAboutCentre(double factor) => ZoomAbout(SquareCentre, factor);

    /// <summary>Moves the portrait by a screen offset (the square stays put).</summary>
    public void MovePortrait(double dx, double dy)
    {
        if (image == null) return;
        var pr = PortraitRect(thumb);
        Edit(thumb with { X = thumb.X - dx / pr.Width, Y = thumb.Y - dy / pr.Height });
    }

    // ---- Mouse ----

    Point last;
    bool dragging, aiming;

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double notches = e.Delta / 120.0;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 1.02 : 1.12;
        ZoomAbout(e.GetPosition(this), Math.Pow(step, notches));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        if (image != null && Rect.Inflate(OverviewRect, 6, 6).Contains(p)) { aiming = true; AimAt(p); }
        else dragging = true;
        last = p;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (aiming) AimAt(p);
        else if (dragging) MovePortrait(p.X - last.X, p.Y - last.Y);
        else Cursor = image != null && Rect.Inflate(OverviewRect, 6, 6).Contains(p) ? Cursors.Cross : Cursors.SizeAll;
        last = p;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        dragging = aiming = false;
        ReleaseMouseCapture();
    }

    void AimAt(Point p)
    {
        var ov = OverviewRect;
        if (ov.IsEmpty) return;
        Edit(thumb with { X = (p.X - ov.X) / ov.Width, Y = (p.Y - ov.Y) / ov.Height });
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        InvalidateVisual();
    }
}
