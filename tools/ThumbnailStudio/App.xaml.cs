using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ThumbnailStudio;

/// <summary>What the window needs to know about this run.</summary>
public sealed record StudioOptions(
    string Repo,
    string ClassesPath,
    string PortraitsDir,
    string? Select,
    bool GitSync,          // commit + push the Thumbnail edits (off with --classes or outside a git checkout)
    bool Deploy,           // redeploy the installed game on close (off with --no-deploy)
    TimeSpan CommitQuiet,  // commit after this long with no further saves
    int DemoUnits,         // --demo [N]
    bool DemoExit,
    string? Notice);

/// <summary>
/// Command line:
///   --repo &lt;path&gt;       the Unity.GridGame checkout (default: walk up from the exe, then the working directory,
///                       until Assets/Gaiden/Resources/Content/classes.json exists)
///   --classes &lt;file&gt;    edit this classes.json instead (portraits still come from the repo; no git, no deploy)
///   --select &lt;id&gt;       open on this class
///   --no-deploy         never start tools\run.ps1 on close (tests)
///   --commit-quiet &lt;s&gt;  seconds without a save before committing (default 20)
///   --demo [N]          frame N units hands-free (default 1); autosave, commit and push run as usual
///   --demo-exit         close the window when the demo is done
///   --no-rebuild-check  don't relaunch through tools\thumbnail-studio.ps1 when the sources are newer than this build
///   --build-failed      (set by the launcher) the rebuild failed and this is the previous build
/// </summary>
public partial class App : Application
{
    public const string ClassesRel = "Assets/Gaiden/Resources/Content/classes.json";
    public const string PortraitsRel = "Assets/Gaiden/Resources/Portraits";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? repo = null, classes = null, select = null;
        bool noDeploy = false, noRebuildCheck = false, buildFailed = false, demoExit = false;
        int demo = 0;
        double quiet = 20;
        for (int i = 0; i < e.Args.Length; i++)
        {
            string a = e.Args[i];
            string? Next() => i + 1 < e.Args.Length ? e.Args[++i] : null;
            if (a is "--repo" or "-repo") repo = Next();
            else if (a is "--classes" or "-classes") classes = Next();
            else if (a is "--select" or "-select") select = Next();
            else if (a is "--no-deploy") noDeploy = true;
            else if (a is "--no-rebuild-check") noRebuildCheck = true;
            else if (a is "--build-failed") buildFailed = true;
            else if (a is "--demo-exit") demoExit = true;
            else if (a is "--commit-quiet" && double.TryParse(Next(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double q)) quiet = Math.Max(0.5, q);
            else if (a is "--demo" or "-demo")
            {
                demo = 1;
                if (i + 1 < e.Args.Length && int.TryParse(e.Args[i + 1], out int n)) { demo = Math.Max(1, n); i++; }
            }
        }

        if (!noRebuildCheck && demo == 0 && RelaunchIfStale(e.Args)) { Shutdown(0); return; }

        repo = repo != null ? Path.GetFullPath(repo) : FindRepo(AppContext.BaseDirectory) ?? FindRepo(Environment.CurrentDirectory);
        if (repo == null || !Directory.Exists(repo))
        {
            Fail("Couldn't find the game repo (a folder holding " + ClassesRel + ").\nRun from inside the repo or pass --repo <path>.");
            return;
        }
        string repoClasses = Path.Combine(repo, ClassesRel.Replace('/', Path.DirectorySeparatorChar));
        classes = classes != null ? Path.GetFullPath(classes) : repoClasses;
        if (!File.Exists(classes))
        {
            Fail("classes.json not found:\n" + classes);
            return;
        }

        bool inRepo = string.Equals(Path.GetFullPath(classes), Path.GetFullPath(repoClasses), StringComparison.OrdinalIgnoreCase);
        bool git = inRepo && GitSync.IsRepo(repo);
        string? notice = buildFailed ? "Rebuild failed - running the previous build (see " + Path.Combine(Log.Dir, "build.log") + ")"
            : !inRepo ? "Git sync and deploy are off: --classes points outside the repo"
            : !git ? "Git sync is off: the repo isn't a git checkout"
            : null;
        Log.Write($"Start: repo={repo} classes={classes} git={git} deploy={git && !noDeploy} demo={demo}");

        try
        {
            var win = new MainWindow(new StudioOptions(repo, classes, Path.Combine(repo, PortraitsRel), select,
                git, git && !noDeploy, TimeSpan.FromSeconds(quiet), demo, demoExit, notice));
            MainWindow = win;
            if (demo > 0) win.Loaded += async (_, _) => await win.RunDemoAsync();
            win.Show();
        }
        catch (Exception ex)
        {
            Fail("Couldn't load " + classes + ":\n" + ex.Message);
        }
    }

    /// <summary>
    /// The desktop shortcut starts this exe directly (no console). When the studio's sources are newer than the
    /// last launcher build (build.stamp beside the exe), hand off to tools\thumbnail-studio.ps1 in a hidden
    /// PowerShell, which waits for this process to exit, rebuilds and starts the new build.
    /// </summary>
    static bool RelaunchIfStale(string[] args)
    {
        try
        {
            string bin = AppContext.BaseDirectory;
            if (!bin.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase)) return false;
            DirectoryInfo? src = new(bin);
            while (src != null && !File.Exists(Path.Combine(src.FullName, "ThumbnailStudio.csproj"))) src = src.Parent;
            if (src == null) return false;
            string launcher = Path.Combine(src.Parent!.FullName, "thumbnail-studio.ps1");
            if (!File.Exists(launcher)) return false;

            string stamp = Path.Combine(bin, "build.stamp");
            DateTime built = File.Exists(stamp) ? File.GetLastWriteTimeUtc(stamp) : DateTime.MinValue;
            bool stale = Directory.EnumerateFiles(src.FullName, "*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(@"\bin\") && !f.Contains(@"\obj\"))
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".cs" or ".xaml" or ".csproj" or ".ico")
                .Any(f => File.GetLastWriteTimeUtc(f) > built);
            if (!stale) return false;

            var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", launcher,
                         "-WaitPid", Environment.ProcessId.ToString() })
                psi.ArgumentList.Add(a);
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
            Log.Write("Sources newer than this build: relaunching through " + launcher);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Rebuild check failed: " + ex.Message);
            return false;
        }
    }

    static string? FindRepo(string start)
    {
        for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, ClassesRel))) return d.FullName;
        return null;
    }

    void Fail(string message)
    {
        MessageBox.Show(message, "Thumbnail Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }
}
