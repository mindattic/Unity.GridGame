using System.IO;
using System.Windows;

namespace ThumbnailStudio;

/// <summary>
/// Command line:
///   --repo &lt;path&gt;     the Unity.GridGame checkout (default: walk up from the exe, then the working directory,
///                     until Assets/Gaiden/Resources/Content/classes.json exists)
///   --classes &lt;file&gt;  edit this classes.json instead (portraits still come from the repo)
///   --select &lt;id&gt;     open on this class
///   --demo            frame, save and step hands-free (for recordings; saves, so use a scratch --classes)
/// </summary>
public partial class App : Application
{
    public const string ClassesRel = "Assets/Gaiden/Resources/Content/classes.json";
    public const string PortraitsRel = "Assets/Gaiden/Resources/Portraits";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? repo = null, classes = null, select = null;
        bool demo = false;
        for (int i = 0; i < e.Args.Length; i++)
        {
            string a = e.Args[i];
            string? Next() => i + 1 < e.Args.Length ? e.Args[++i] : null;
            if (a is "--repo" or "-repo") repo = Next();
            else if (a is "--classes" or "-classes") classes = Next();
            else if (a is "--select" or "-select") select = Next();
            else if (a is "--demo" or "-demo") demo = true;
        }

        repo = repo != null ? Path.GetFullPath(repo) : FindRepo(AppContext.BaseDirectory) ?? FindRepo(Environment.CurrentDirectory);
        if (repo == null || !Directory.Exists(repo))
        {
            Fail("Couldn't find the game repo (a folder holding " + ClassesRel + ").\nRun from inside the repo or pass --repo <path>.");
            return;
        }
        classes = classes != null ? Path.GetFullPath(classes) : Path.Combine(repo, ClassesRel);
        if (!File.Exists(classes))
        {
            Fail("classes.json not found:\n" + classes);
            return;
        }

        try
        {
            var win = new MainWindow(classes, Path.Combine(repo, PortraitsRel), select);
            MainWindow = win;
            if (demo) win.Loaded += async (_, _) => await win.RunDemoAsync();
            win.Show();
        }
        catch (Exception ex)
        {
            Fail("Couldn't load " + classes + ":\n" + ex.Message);
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
