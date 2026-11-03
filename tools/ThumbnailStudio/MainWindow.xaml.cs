using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    readonly StudioOptions opt;
    readonly string classesPath;
    readonly ObservableCollection<UnitEntry> units = new();
    readonly ICollectionView view;
    UnitEntry? unit;
    readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    // GG-T223: no Save step. Any framing change autosaves (debounced); saves are committed after a quiet period
    // and pushed; a failed push retries every minute; closing flushes all of it and starts the redeploy.
    readonly GitSync? git;
    readonly DispatcherTimer autosaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly DispatcherTimer commitTimer = new();
    readonly DispatcherTimer pushRetryTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    bool syncing, closing, closeReady, forceClose;

    public MainWindow(StudioOptions options)
    {
        InitializeComponent();
        opt = options;
        classesPath = options.ClassesPath;
        FileText.Text = classesPath;
        FileText.ToolTip = classesPath;
        if (options.GitSync) git = new GitSync(options.Repo, App.ClassesRel);

        foreach (var c in ClassesFile.Load(classesPath))
        {
            var u = new UnitEntry(c, Path.Combine(options.PortraitsDir, c.Id.ToLowerInvariant() + ".png"));
            u.PropertyChanged += AnyUnit_PropertyChanged;
            units.Add(u);
        }
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
        autosaveTimer.Tick += (_, _) => { autosaveTimer.Stop(); FlushSaves(); };
        commitTimer.Interval = options.CommitQuiet;
        commitTimer.Tick += async (_, _) => { commitTimer.Stop(); await CommitAndPushAsync(); };
        pushRetryTimer.Tick += async (_, _) => { if (git?.PushPending == true && !syncing) await CommitAndPushAsync(); };

        SetSync(options.Notice ?? (git != null
            ? "Edits save themselves, then get committed and pushed; closing redeploys the installed game."
            : "Edits save themselves to " + classesPath));

        UpdateCount();
        var first = (options.Select != null ? units.FirstOrDefault(u => string.Equals(u.Id, options.Select, StringComparison.OrdinalIgnoreCase)) : null)
                    ?? units.FirstOrDefault(u => u.HasPortrait) ?? units.FirstOrDefault();
        UnitList.SelectedItem = first;
        if (first != null) UnitList.ScrollIntoView(first);
        Loaded += async (_, _) =>
        {
            Editor.Focus();
            // A push that didn't make it last session (offline, remote moved) goes out now.
            if (git != null && await Task.Run(git.HasUnpushedStudioCommits))
            {
                SetSync("Pushing studio commits left over from last time...");
                await PushAsync();
            }
        };
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
        int shown = units.Count(Matches), noThumb = units.Count(u => !u.HasSavedThumb);
        int editing = units.Count(u => u.IsDirty), session = units.Count(u => u.State >= SyncState.Saved);
        CountText.Text = $"{shown} of {units.Count} units" + (editing > 0 ? $" · {editing} editing" : "")
            + (session > 0 ? $" · {session} reframed this session" : "") + (noThumb > 0 ? $" · {noThumb} without a saved thumbnail" : "");
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
        if (e.PropertyName is nameof(UnitEntry.Current) or nameof(UnitEntry.IsDirty) or nameof(UnitEntry.State)) SyncAll();
    }

    /// <summary>Every unit: a framing change marks it "editing" and (re)starts the autosave debounce.</summary>
    void AnyUnit_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not UnitEntry u || e.PropertyName != nameof(UnitEntry.IsDirty) && e.PropertyName != nameof(UnitEntry.Current)) return;
        if (!u.IsDirty) return;
        u.State = SyncState.Editing;
        commitTimer.Stop();   // still editing: hold the commit until things are quiet
        autosaveTimer.Stop();
        autosaveTimer.Start();
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
        UnitState.Text = unit.StateLabel;
        UnitState.Foreground = unit.State == SyncState.Editing ? (System.Windows.Media.Brush)FindResource("Accent") : (System.Windows.Media.Brush)FindResource("Muted");
        ResetButton.IsEnabled = unit.Current.Rounded() != unit.Original.Rounded();
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
    /// --demo [N]: frames N units by themselves through the same operations the mouse and keys use (a drag, wheel
    /// zoom toward the face, arrow nudges), stepping to the next unit after each. Autosave, commit and push run
    /// exactly as for a person. Test it against a scratch clone (--repo) with --no-deploy.
    /// </summary>
    public async Task RunDemoAsync()
    {
        await Task.Delay(1500);
        for (int k = 0; k < opt.DemoUnits; k++)
        {
            // Drag the head (about 14% down the portrait) into the middle of the square, as a hand would.
            const double fx = 0.5, fy = 0.14;
            var from = Editor.ScreenOf(fx, fy);
            var to = Editor.SquareCentre;
            for (int i = 0; i < 25; i++) { Editor.MovePortrait((to.X - from.X) / 25, (to.Y - from.Y) / 25); await Task.Delay(40); }
            await Task.Delay(500);
            for (int i = 0; i < 10; i++) { Editor.ZoomAbout(Editor.ScreenOf(fx, fy), 1.07); await Task.Delay(120); }
            await Task.Delay(600);
            for (int i = 0; i < 3; i++) { Editor.MovePortrait(-1, 0); await Task.Delay(250); }
            Editor.MovePortrait(0, -10 - k);
            await Task.Delay(1500);   // autosave fires 600 ms after the last change
            if (k < opt.DemoUnits - 1) { Step(+1); await Task.Delay(1500); }
        }
        if (opt.DemoExit)
        {
            await Task.Delay(1000);
            Close();
        }
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

    /// <summary>Writes every unit with an unsaved framing to classes.json (only its Thumbnail block). Errors go to the status bar.</summary>
    bool FlushSaves()
    {
        autosaveTimer.Stop();
        bool ok = true;
        foreach (var u in units.Where(u => u.IsDirty).ToList())
        {
            try
            {
                ClassesFile.SaveThumbnail(classesPath, u.Id, u.Current);
                u.MarkSaved();
                u.State = SyncState.Saved;
                git?.Enqueue(u.Id, u.Name, u.Saved);
                Log.Write($"Saved {u.Id}: {u.Saved}");
            }
            catch (Exception ex)
            {
                ok = false;
                Log.Write($"Save {u.Id} failed: {ex.Message}");
                SetSync($"Couldn't save {u.Name}: {ex.Message}", error: true);
            }
        }
        if (git?.HasPending == true && !closing)
        {
            commitTimer.Stop();
            commitTimer.Start();
            if (ok) SetSync($"Saved. Committing after {opt.CommitQuiet.TotalSeconds:0} s without edits...");
        }
        SyncAll();
        return ok;
    }

    /// <summary>Commits the pending saves (if any) and pushes. Never blocks the UI thread; errors go to the status bar.</summary>
    async Task CommitAndPushAsync()
    {
        if (git == null || syncing) return;
        syncing = true;
        try
        {
            if (git.HasPending)
            {
                SetSync("Committing...");
                var r = await git.CommitAsync();
                if (r != null)
                {
                    foreach (var u in units.Where(u => r.Ids.Contains(u.Id) && u.State == SyncState.Saved && !u.IsDirty))
                        u.State = SyncState.Committed;
                    string what = r.Commit != null ? $"Committed {r.Ids.Count} unit(s) ({r.Commit[..7]})" : "Already in HEAD";
                    if (r.NotInHead.Count > 0) what += $"; {string.Join(", ", r.NotInHead)} not in HEAD yet, left saved";
                    if (r.IndexNote != null) what += "; " + r.IndexNote;
                    SetSync(what);
                }
            }
            if (git.PushPending) await PushAsync();
        }
        catch (Exception ex)
        {
            Log.Write("Commit failed: " + ex.Message);
            SetSync("Commit failed (will retry): " + ex.Message, error: true);
            commitTimer.Stop();
            commitTimer.Start();
        }
        finally
        {
            syncing = false;
        }
    }

    async Task PushAsync()
    {
        if (git == null) return;
        SetSync("Pushing...");
        var p = await git.PushAsync();
        switch (p.Outcome)
        {
            case PushOutcome.Pushed or PushOutcome.NothingToPush:
                foreach (var u in units.Where(u => u.State == SyncState.Committed)) u.State = SyncState.Pushed;
                pushRetryTimer.Stop();
                SetSync("Pushed: every saved framing is in origin/main.");
                break;
            case PushOutcome.Skipped:
                pushRetryTimer.Stop();
                SetSync("Committed; " + p.Message, error: true);
                break;
            default:
                if (!closing) pushRetryTimer.Start();
                SetSync(char.ToUpper(p.Message[0]) + p.Message[1..] + (closing ? " (the next studio session retries the push)" : " (retrying every minute)"), error: true);
                break;
        }
    }

    void SetSync(string s, bool error = false)
    {
        SyncText.Text = s;
        SyncText.ToolTip = s;
        SyncText.Foreground = error ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0x8A, 0x6C))
                                    : (System.Windows.Media.Brush)FindResource("Fg");
        if (FinishingText != null && closing) FinishingText.Text += s + "\n";
    }

    void Status(string s)
    {
        StatusText.Text = s;
        statusTimer.Stop();
        statusTimer.Start();
    }

    void Prev_Click(object sender, RoutedEventArgs e) => Step(-1);
    void Next_Click(object sender, RoutedEventArgs e) => Step(+1);
    void Reset_Click(object sender, RoutedEventArgs e) { if (unit != null) unit.Current = unit.Original; Editor.Focus(); }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool inText = Keyboard.FocusedElement is TextBox;
        if (closing) { e.Handled = true; return; }
        if (e.Key == Key.S && mods.HasFlag(ModifierKeys.Control))
        {
            CommitBoxes();
            if (FlushSaves()) Status("Saved");
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

    // ---- Close: save, commit, push, redeploy ----

    protected override void OnClosing(CancelEventArgs e)
    {
        if (closeReady || forceClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        if (!closing) _ = FinishAndCloseAsync();
    }

    async Task FinishAndCloseAsync()
    {
        closing = true;
        commitTimer.Stop();
        pushRetryTimer.Stop();
        FinishingText.Text = "";
        Finishing.Visibility = Visibility.Visible;
        CommitBoxes();
        if (!FlushSaves())
        {
            // Don't drop an edit silently: stay open, and let a second close quit without it.
            closing = false;
            forceClose = true;
            Finishing.Visibility = Visibility.Collapsed;
            SetSync(SyncText.Text + " - close again to quit without it.", error: true);
            return;
        }
        SetSync("All framings saved to classes.json.");

        if (git != null)
        {
            while (syncing) await Task.Delay(100);
            await CommitAndPushAsync();
            if (git.HasPending) SetSync("Some framings are saved but not committed (see the log); the next session commits them.");
        }

        if (git != null && git.CommitsThisSession > 0)
        {
            string msg = await Task.Run(StartDeploy);
            SetSync(msg);
            Log.Write("Close: " + msg);
            await Task.Delay(3500);
        }
        else await Task.Delay(800);

        closeReady = true;
        Close();
    }

    /// <summary>
    /// Rebuilds and republishes the installed game (tools\run.ps1) in its own minimized PowerShell that outlives
    /// the studio, unless the game or the Unity Editor is running (run.ps1 would kill the one and can't build
    /// beside the other); then the next Deploy &amp; Run picks the change up, since run.ps1 rebuilds whenever Assets changed.
    /// </summary>
    string StartDeploy()
    {
        string? blocker = DeployBlocker();
        if (blocker != null) return $"Not redeploying now: {blocker}. The next Deploy & Run picks the new thumbnails up.";
        if (!opt.Deploy) return "Would start tools\\run.ps1 now; skipped (--no-deploy).";
        try
        {
            Directory.CreateDirectory(Log.Dir);
            string log = Path.Combine(Log.Dir, $"deploy-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            string runPs1 = Path.Combine(opt.Repo, "tools", "run.ps1");
            static string Q(string s) => "'" + s.Replace("'", "''") + "'";
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
                WorkingDirectory = opt.Repo,
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"& " + Q(runPs1) + " *>&1 | Out-File -FilePath " + Q(log) + " -Encoding utf8\"",
            };
            Process.Start(psi);
            Dispatcher.Invoke(() => { foreach (var u in units.Where(u => u.State is SyncState.Committed or SyncState.Pushed)) u.State = SyncState.DeployStarted; });
            return $"Deploy started: tools\\run.ps1 is rebuilding the installed game in the background (log: {log}).";
        }
        catch (Exception ex)
        {
            return "Couldn't start tools\\run.ps1: " + ex.Message;
        }
    }

    string? DeployBlocker()
    {
        string apps = @"C:\Apps\GridGameGaiden\", build = Path.Combine(opt.Repo, "Build") + "\\";
        foreach (var p in Process.GetProcessesByName("GridGameGaiden"))
        {
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { }
            if (path == null || path.StartsWith(apps, StringComparison.OrdinalIgnoreCase) || path.StartsWith(build, StringComparison.OrdinalIgnoreCase))
                return "GridGameGaiden is running";
        }
        if (Process.GetProcessesByName("Unity").Length > 0) return "the Unity Editor is running";
        return null;
    }
}
