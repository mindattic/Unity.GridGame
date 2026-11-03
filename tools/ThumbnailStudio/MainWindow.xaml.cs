using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace ThumbnailStudio;

public partial class MainWindow : Window
{
    readonly string classesPath;
    readonly ObservableCollection<UnitEntry> units = new();
    readonly ICollectionView view;
    UnitEntry? unit;
    readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    public MainWindow(string classesPath, string portraitsDir, string? select)
    {
        InitializeComponent();
        this.classesPath = classesPath;
        FileText.Text = classesPath;
        FileText.ToolTip = classesPath;

        foreach (var c in ClassesFile.Load(classesPath))
            units.Add(new UnitEntry(c, Path.Combine(portraitsDir, c.Id.ToLowerInvariant() + ".png")));
        view = CollectionViewSource.GetDefaultView(units);
        view.Filter = o => Matches((UnitEntry)o);
        UnitList.ItemsSource = view;

        Editor.ThumbEdited += t => { if (unit != null) unit.Current = t; };
        foreach (var box in new[] { ZoomBox, XBox, YBox })
        {
            box.LostKeyboardFocus += (_, _) => CommitBoxes();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { CommitBoxes(); Editor.Focus(); e.Handled = true; }
                else if (e.Key == Key.Escape) { SyncBoxes(); Editor.Focus(); e.Handled = true; }
            };
        }
        statusTimer.Tick += (_, _) => { StatusText.Text = ""; statusTimer.Stop(); };

        UpdateCount();
        var first = (select != null ? units.FirstOrDefault(u => string.Equals(u.Id, select, StringComparison.OrdinalIgnoreCase)) : null)
                    ?? units.FirstOrDefault(u => u.HasPortrait) ?? units.FirstOrDefault();
        UnitList.SelectedItem = first;
        if (first != null) UnitList.ScrollIntoView(first);
        Loaded += (_, _) => Editor.Focus();
    }

    // ---- List ----

    bool Matches(UnitEntry u)
    {
        if (FilterHeroes?.IsChecked == true && !u.IsHero) return false;
        if (FilterEnemies?.IsChecked == true && u.IsHero) return false;
        string q = SearchBox?.Text.Trim() ?? "";
        return q.Length == 0 || u.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || u.Id.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    void Refilter()
    {
        if (view == null) return;
        view.Refresh();
        UpdateCount();
        if (unit != null && Matches(unit)) UnitList.SelectedItem = unit;
    }

    void UpdateCount()
    {
        int shown = units.Count(Matches), dirty = units.Count(u => u.IsDirty), noThumb = units.Count(u => !u.HasSavedThumb);
        CountText.Text = $"{shown} of {units.Count} units" + (dirty > 0 ? $" · {dirty} unsaved" : "") + (noThumb > 0 ? $" · {noThumb} without a saved thumbnail" : "");
    }

    void Filter_Changed(object sender, RoutedEventArgs e) => Refilter();

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Refilter();
    }

    void UnitList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UnitList.SelectedItem is UnitEntry u && u != unit) Show(u);
    }

    // ---- Selected unit ----

    void Show(UnitEntry u)
    {
        CommitBoxes();
        if (unit != null) unit.PropertyChanged -= Unit_PropertyChanged;
        unit = u;
        unit.PropertyChanged += Unit_PropertyChanged;

        var img = Portraits.LoadFull(u.PortraitPath);
        Editor.Image = img;
        Editor.EmptyText = $"Portrait missing: {Path.GetFileName(u.PortraitPath)}";
        foreach (var p in new[] { TokenPreview, TimelinePreview, TimelineSmall, CardPreview })
        {
            p.Source = img;
            p.IsHero = u.IsHero;
            p.IsBoss = u.IsBoss;
        }
        UnitName.Text = u.Name;
        UnitSub.Text = $"{u.Id} · {u.TeamLabel}" + (u.HasPortrait ? "" : " · no portrait png");
        CardName.Text = u.Name;
        CardTeam.Text = u.TeamLabel;
        UnitList.ScrollIntoView(u);
        SyncAll();
    }

    void Unit_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UnitEntry.Current) or nameof(UnitEntry.IsDirty)) SyncAll();
    }

    void SyncAll()
    {
        if (unit == null) return;
        var t = unit.Current;
        Editor.Thumb = t;
        foreach (var p in new[] { TokenPreview, TimelinePreview, TimelineSmall, CardPreview })
        {
            p.Zoom = t.Zoom;
            p.XFrac = t.X;
            p.YFrac = t.Y;
        }
        SyncBoxes();
        DirtyDot.Visibility = unit.IsDirty ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = unit.IsDirty || !unit.HasSavedThumb;
        ResetButton.IsEnabled = unit.IsDirty;
        SavedText.Text = unit.HasSavedThumb ? unit.Saved.ToString() : "none yet (default " + Thumb.Default + ")";
        UpdateCount();
    }

    void SyncBoxes()
    {
        if (unit == null) return;
        var t = unit.Current;
        if (!ZoomBox.IsKeyboardFocused) ZoomBox.Text = Thumb.Num(t.Zoom);
        if (!XBox.IsKeyboardFocused) XBox.Text = Thumb.Num(t.X);
        if (!YBox.IsKeyboardFocused) YBox.Text = Thumb.Num(t.Y);
    }

    void CommitBoxes()
    {
        if (unit == null) return;
        var t = unit.Current;
        static double Parse(string s, double fallback) =>
            double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : fallback;
        unit.Current = new Thumb(Parse(ZoomBox.Text, t.Zoom), Parse(XBox.Text, t.X), Parse(YBox.Text, t.Y));
        // Show the clamped values even when nothing changed.
        ZoomBox.Text = Thumb.Num(unit.Current.Zoom);
        XBox.Text = Thumb.Num(unit.Current.X);
        YBox.Text = Thumb.Num(unit.Current.Y);
    }

    /// <summary>
    /// --demo: frames the selected unit by itself through the same operations the mouse and keys use (wheel zoom
    /// toward the upper part of the square, a drag, arrow nudges), then saves it and steps to the next unit. Used
    /// to record the tool hands-free; it saves, so point --classes at a scratch copy.
    /// </summary>
    public async Task RunDemoAsync()
    {
        await Task.Delay(1500);
        // Drag the head (about 14% down the portrait) into the middle of the square, as a hand would.
        const double fx = 0.5, fy = 0.14;
        var from = Editor.ScreenOf(fx, fy);
        var to = Editor.SquareCentre;
        for (int i = 0; i < 25; i++) { Editor.MovePortrait((to.X - from.X) / 25, (to.Y - from.Y) / 25); await Task.Delay(40); }
        await Task.Delay(500);
        // Wheel-zoom in at the face.
        for (int i = 0; i < 10; i++) { Editor.ZoomAbout(Editor.ScreenOf(fx, fy), 1.07); await Task.Delay(120); }
        await Task.Delay(600);
        for (int i = 0; i < 3; i++) { Editor.MovePortrait(-1, 0); await Task.Delay(250); }
        Editor.MovePortrait(0, -10);
        await Task.Delay(1500);
        SaveCurrent();
        await Task.Delay(2500);
        Step(+1);
        await Task.Delay(2000);
    }

    // ---- Commands ----

    void Step(int dir)
    {
        var list = view.Cast<UnitEntry>().ToList();
        if (list.Count == 0) return;
        int i = unit != null ? list.IndexOf(unit) : -1;
        i = i < 0 ? 0 : Math.Clamp(i + dir, 0, list.Count - 1);
        UnitList.SelectedItem = list[i];
    }

    bool Save(UnitEntry u)
    {
        try
        {
            ClassesFile.SaveThumbnail(classesPath, u.Id, u.Current);
            u.MarkSaved();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save {u.Id}:\n{ex.Message}", "Thumbnail Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    void SaveCurrent()
    {
        CommitBoxes();
        if (unit == null || !unit.HasPortrait && !unit.IsDirty) return;
        if (Save(unit)) Status($"Saved {unit.Id}: {unit.Saved}");
        SyncAll();
    }

    void SaveAll()
    {
        CommitBoxes();
        var dirty = units.Where(u => u.IsDirty).ToList();
        int ok = dirty.Count(Save);
        Status($"Saved {ok} of {dirty.Count} unsaved units");
        SyncAll();
    }

    void Status(string s)
    {
        StatusText.Text = s;
        statusTimer.Stop();
        statusTimer.Start();
    }

    void Prev_Click(object sender, RoutedEventArgs e) => Step(-1);
    void Next_Click(object sender, RoutedEventArgs e) => Step(+1);
    void Save_Click(object sender, RoutedEventArgs e) => SaveCurrent();
    void Reset_Click(object sender, RoutedEventArgs e) { unit?.Reset(); Editor.Focus(); }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool inText = Keyboard.FocusedElement is TextBox;
        if (e.Key == Key.S && mods.HasFlag(ModifierKeys.Control))
        {
            if (mods.HasFlag(ModifierKeys.Shift)) SaveAll(); else SaveCurrent();
            e.Handled = true;
        }
        else if (e.Key == Key.PageUp) { Step(-1); e.Handled = true; }
        else if (e.Key == Key.PageDown) { Step(+1); e.Handled = true; }
        else if (!inText)
        {
            double n = mods.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            switch (e.Key)
            {
                case Key.Left: Editor.MovePortrait(-n, 0); e.Handled = true; break;
                case Key.Right: Editor.MovePortrait(n, 0); e.Handled = true; break;
                case Key.Up: Editor.MovePortrait(0, -n); e.Handled = true; break;
                case Key.Down: Editor.MovePortrait(0, n); e.Handled = true; break;
                case Key.OemPlus or Key.Add: Editor.ZoomAboutCentre(mods.HasFlag(ModifierKeys.Shift) ? 1.25 : 1.05); e.Handled = true; break;
                case Key.OemMinus or Key.Subtract: Editor.ZoomAboutCentre(mods.HasFlag(ModifierKeys.Shift) ? 1 / 1.25 : 1 / 1.05); e.Handled = true; break;
            }
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        CommitBoxes();
        int dirty = units.Count(u => u.IsDirty);
        if (dirty > 0)
        {
            var r = MessageBox.Show(this, $"{dirty} unit(s) have unsaved thumbnail edits.\n\nSave them before closing?",
                "Thumbnail Studio", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) e.Cancel = true;
            else if (r == MessageBoxResult.Yes)
            {
                SaveAll();
                if (units.Any(u => u.IsDirty)) e.Cancel = true;
            }
        }
        base.OnClosing(e);
    }
}
