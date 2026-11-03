using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ThumbnailStudio;

/// <summary>One class in the list: its saved thumbnail, the edit in progress, and its portrait.</summary>
public sealed class UnitEntry : INotifyPropertyChanged
{
    public UnitEntry(ClassInfo info, string portraitPath)
    {
        Id = info.Id;
        Name = info.Name;
        Team = info.Team;
        IsBoss = info.IsBoss;
        PortraitPath = portraitPath;
        HasPortrait = File.Exists(portraitPath);
        saved = info.Thumbnail;
        current = info.Thumbnail ?? Thumb.Default;
        Original = current;
    }

    public string Id { get; }
    public string Name { get; }
    public string Team { get; }
    public bool IsHero => Team == "Hero";
    public bool IsBoss { get; }
    public string PortraitPath { get; }
    public bool HasPortrait { get; }
    public string TeamLabel => IsBoss ? Team + " · boss" : Team;

    Thumb? saved;
    Thumb current;

    /// <summary>What classes.json holds (or the default when it holds nothing).</summary>
    public Thumb Saved => saved ?? Thumb.Default;
    public bool HasSavedThumb => saved.HasValue;

    public Thumb Current
    {
        get => current;
        set
        {
            value = value.Clamped();
            if (value == current) return;
            current = value;
            Raise(); Raise(nameof(Zoom)); Raise(nameof(X)); Raise(nameof(Y)); Raise(nameof(IsDirty));
        }
    }

    public double Zoom => current.Zoom;
    public double X => current.X;
    public double Y => current.Y;

    /// <summary>The edit differs from what is on disk (compared at the 4 decimals that would be written).</summary>
    public bool IsDirty => current.Rounded() != Saved.Rounded();

    public void MarkSaved()
    {
        saved = current.Rounded();
        current = saved.Value;
        Raise(nameof(Current)); Raise(nameof(Zoom)); Raise(nameof(X)); Raise(nameof(Y));
        Raise(nameof(IsDirty)); Raise(nameof(HasSavedThumb)); Raise(nameof(Saved));
    }

    public void Reset() => Current = Saved;

    /// <summary>What classes.json held when the studio opened (Revert goes back to it; autosave then writes it).</summary>
    public Thumb Original { get; }

    SyncState state;

    /// <summary>Editing → saved (classes.json) → committed → pushed → deploy started (GG-T223).</summary>
    public SyncState State
    {
        get => state;
        set { if (state == value) return; state = value; Raise(); Raise(nameof(StateLabel)); }
    }

    public string StateLabel => state switch
    {
        SyncState.Editing => "editing",
        SyncState.Saved => "saved",
        SyncState.Committed => "committed",
        SyncState.Pushed => "pushed",
        SyncState.DeployStarted => "deploying",
        _ => "",
    };

    BitmapSource? listImage;
    bool listImageTried;

    /// <summary>A downscaled portrait for the list rows (keeps 179 portraits cheap).</summary>
    public BitmapSource? ListImage
    {
        get
        {
            if (!listImageTried) { listImageTried = true; listImage = Portraits.Load(PortraitPath, decodeWidth: 220); }
            return listImage;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Portrait loading: full-resolution images are cached for the few units that get edited.</summary>
public static class Portraits
{
    static readonly Dictionary<string, BitmapSource?> Full = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource? LoadFull(string path)
    {
        if (!Full.TryGetValue(path, out var img)) Full[path] = img = Load(path, 0);
        return img;
    }

    public static BitmapSource? Load(string path, int decodeWidth)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
