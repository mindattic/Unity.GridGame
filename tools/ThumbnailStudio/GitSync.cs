using System.Diagnostics;
using System.IO;
using System.Text;

namespace ThumbnailStudio;

/// <summary>Where one unit's framing is on its way into the game (GG-T223).</summary>
public enum SyncState { None, Editing, Saved, Committed, Pushed, DeployStarted }

public sealed record CommitResult(string? Commit, IReadOnlyList<string> Ids, IReadOnlyList<string> NotInHead, string? IndexNote);

public enum PushOutcome { Pushed, NothingToPush, RemoteMoved, Failed, Skipped }

public sealed record PushResult(PushOutcome Outcome, string Message);

/// <summary>
/// Commits and pushes the studio's Thumbnail edits without touching anyone else's work in a shared checkout
/// (GG-T223). The working-tree classes.json may also hold other people's unstaged hunks, and the real index
/// may hold their staged files, so the commit is built with plumbing: HEAD's classes.json blob plus only the
/// studio's Thumbnail blocks (the same targeted edit autosave made), written into a temporary index, committed
/// with commit-tree on top of HEAD and installed with a compare-and-swap update-ref. The real index entry for
/// classes.json follows only when nobody had staged anything there. Pushes never force, rebase or merge.
/// </summary>
public sealed class GitSync
{
    public const string Trailer =
        "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\n" +
        "Claude-Session: https://claude.ai/code/session_014FAidJDerAYKqngUDG8Pg8";

    readonly string repo;
    readonly string rel;   // classes.json, repo-relative with forward slashes
    readonly SemaphoreSlim gate = new(1, 1);
    readonly object sync = new();
    readonly Dictionary<string, (string Name, Thumb Thumb)> pending = new(StringComparer.Ordinal);

    public GitSync(string repo, string relPath)
    {
        this.repo = repo;
        rel = relPath.Replace('\\', '/');
    }

    /// <summary>Saved in the working tree, not yet committed.</summary>
    public bool HasPending { get { lock (sync) return pending.Count > 0; } }

    /// <summary>A commit is in HEAD that origin/main doesn't have yet.</summary>
    public bool PushPending { get; private set; }

    public int CommitsThisSession { get; private set; }

    /// <summary>Records that <paramref name="id"/>'s Thumbnail was written to the working tree as <paramref name="thumb"/>.</summary>
    public void Enqueue(string id, string name, Thumb thumb)
    {
        lock (sync) pending[id] = (name, thumb.Rounded());
    }

    /// <summary>Whether <paramref name="repoPath"/> is the top of a git work tree.</summary>
    public static bool IsRepo(string repoPath)
    {
        try
        {
            var r = Git.Run(repoPath, ["rev-parse", "--show-toplevel"]);
            return r.Ok && string.Equals(Path.GetFullPath(r.Text.Trim()).TrimEnd('\\'), Path.GetFullPath(repoPath).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Commits every pending Thumbnail as one commit on HEAD. Runs on a worker thread.</summary>
    public async Task<CommitResult?> CommitAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { return await Task.Run(CommitCore).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    CommitResult? CommitCore()
    {
        Dictionary<string, (string Name, Thumb Thumb)> snap;
        lock (sync) snap = new(pending);
        if (snap.Count == 0) return null;

        for (int attempt = 1; attempt <= 6; attempt++)
        {
            string oldHead = Git.Run(repo, ["rev-parse", "--verify", "HEAD"]).Check("rev-parse HEAD").Text.Trim();
            // "<mode> blob <sha>\t<path>"
            string ls = Git.Run(repo, ["ls-tree", oldHead, "--", rel]).Check("ls-tree").Text.Trim();
            if (ls.Length == 0) throw new InvalidOperationException($"{rel} is not in HEAD.");
            var parts = ls.Split('\t')[0].Split(' ');
            string mode = parts[0], oldBlob = parts[2];
            byte[] head = Git.Run(repo, ["cat-file", "blob", oldBlob]).Check("cat-file").Stdout;

            byte[] next = head;
            var ids = new List<string>();
            var missing = new List<string>();
            foreach (var (id, (_, thumb)) in snap)
            {
                if (!ClassesFile.HasClass(next, id)) { missing.Add(id); continue; }
                next = ClassesFile.ApplyThumbnail(next, id, thumb, "HEAD:" + rel);
                ids.Add(id);
            }

            if (next.AsSpan().SequenceEqual(head))
            {
                // Already what HEAD holds (e.g. reframed back to where it was): nothing to commit.
                Done(snap, ids);
                return new CommitResult(null, ids, missing, null);
            }

            string newBlob = Git.Run(repo, ["hash-object", "-w", "--no-filters", "--stdin"], next).Check("hash-object").Text.Trim();

            string tmpIndex = Path.Combine(Path.GetTempPath(), $"thumbstudio-index-{Environment.ProcessId}-{Guid.NewGuid():N}");
            string tree;
            try
            {
                var env = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = tmpIndex };
                Git.Run(repo, ["read-tree", oldHead], env: env).Check("read-tree");
                Git.Run(repo, ["update-index", "--cacheinfo", $"{mode},{newBlob},{rel}"], env: env).Check("update-index (temp)");
                tree = Git.Run(repo, ["write-tree"], env: env).Check("write-tree").Text.Trim();
            }
            finally
            {
                try { File.Delete(tmpIndex); } catch { }
            }

            string subject = Subject(ids.Select(id => snap[id].Name).ToList());
            string message = subject + "\n\n" + Trailer + "\n";
            string commit = Git.Run(repo, ["commit-tree", tree, "-p", oldHead, "-F", "-"], Encoding.UTF8.GetBytes(message))
                .Check("commit-tree").Text.Trim();

            // Compare-and-swap: only if HEAD is still the commit we built on.
            var cas = Git.Run(repo, ["update-ref", "-m", "commit: " + subject, "HEAD", commit, oldHead]);
            if (!cas.Ok)
            {
                Log.Write($"HEAD moved while committing (attempt {attempt}): {cas.Error.Trim()} - rebuilding on the new HEAD");
                Thread.Sleep(150 * attempt);
                continue;
            }

            string? note = FollowRealIndex(mode, oldBlob, newBlob);
            Done(snap, ids);
            CommitsThisSession++;
            PushPending = true;
            Log.Write($"Committed {commit[..10]} {subject}" + (missing.Count > 0 ? $" (not in HEAD yet, left uncommitted: {string.Join(", ", missing)})" : ""));
            return new CommitResult(commit, ids, missing, note);
        }
        throw new InvalidOperationException("HEAD kept moving; will retry.");
    }

    /// <summary>
    /// Points the real index's classes.json entry at the new blob when it was HEAD's old blob (nobody had
    /// staged anything there), so git status shows no phantom reverse change. Staged hunks are left alone.
    /// </summary>
    string? FollowRealIndex(string mode, string oldBlob, string newBlob)
    {
        for (int i = 0; i < 20; i++)
        {
            var ls = Git.Run(repo, ["ls-files", "-s", "--", rel]);
            if (!ls.Ok) return Note("couldn't read the index: " + ls.Error.Trim());
            var lines = ls.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length != 1) return Note("classes.json has a merge conflict or isn't in the index; index left alone");
            var f = lines[0].Split('\t')[0].Split(' ');   // "<mode> <sha> <stage>"
            if (f[1] != oldBlob) return Note("classes.json has staged changes; index left alone (they stay staged)");
            var up = Git.Run(repo, ["update-index", "--cacheinfo", $"{mode},{newBlob},{rel}"]);
            if (up.Ok) return null;
            if (!up.Error.Contains("index.lock")) return Note("couldn't update the index: " + up.Error.Trim());
            Thread.Sleep(250);   // someone else holds the index lock
        }
        return Note("the index stayed locked; index left alone");

        static string Note(string s) { Log.Write("Index: " + s); return s; }
    }

    void Done(Dictionary<string, (string Name, Thumb Thumb)> snap, List<string> ids)
    {
        lock (sync)
            foreach (var id in ids)
                if (pending.TryGetValue(id, out var p) && p.Thumb == snap[id].Thumb) pending.Remove(id);
    }

    public static string Subject(IReadOnlyList<string> names)
    {
        const int max = 6;
        string list = string.Join(", ", names.Take(max)) + (names.Count > max ? $" +{names.Count - max} more" : "");
        return $"Thumbnail Studio: reframe {list} (GG-T223)";
    }

    /// <summary>The checked-out branch's short name, or null when HEAD is detached.</summary>
    string? Branch()
    {
        var r = Git.Run(repo, ["symbolic-ref", "-q", "--short", "HEAD"]);
        return r.Ok ? r.Text.Trim() : null;
    }

    /// <summary>
    /// Pushes HEAD to origin/main. Never forces. When origin moved, fetches and pushes only if HEAD still
    /// contains origin/main; otherwise reports RemoteMoved and leaves PushPending set for a later retry.
    /// </summary>
    public async Task<PushResult> PushAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { return await Task.Run(PushCore).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    PushResult PushCore()
    {
        if (Branch() != "main")
        {
            PushPending = false;
            return new PushResult(PushOutcome.Skipped, "not on main; committed locally, not pushed");
        }
        var timeout = TimeSpan.FromSeconds(90);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var p = Git.Run(repo, ["push", "origin", "HEAD:main"], timeout: timeout);
            if (p.Ok)
            {
                PushPending = false;
                bool none = (p.Error + p.Text).Contains("Everything up-to-date");
                Log.Write("Push: " + (none ? "up to date" : "pushed"));
                return new PushResult(none ? PushOutcome.NothingToPush : PushOutcome.Pushed, none ? "origin/main is up to date" : "pushed to origin/main");
            }
            string err = p.Error.Trim();
            Log.Write("Push failed: " + err);
            bool rejected = err.Contains("rejected") || err.Contains("non-fast-forward") || err.Contains("fetch first");
            if (!rejected)
                return new PushResult(PushOutcome.Failed, "push failed: " + LastLine(err));

            var fetch = Git.Run(repo, ["fetch", "origin", "main"], timeout: timeout);
            if (!fetch.Ok) return new PushResult(PushOutcome.Failed, "push rejected and fetch failed: " + LastLine(fetch.Error));
            // HEAD still contains origin/main: it's a plain fast-forward, try again once.
            var anc = Git.Run(repo, ["merge-base", "--is-ancestor", "origin/main", "HEAD"]);
            if (!anc.Ok)
                return new PushResult(PushOutcome.RemoteMoved, "saved and committed locally; push pending - remote moved");
        }
        return new PushResult(PushOutcome.Failed, "push failed twice; will retry");
    }

    /// <summary>Studio commits in HEAD that origin/main doesn't have (e.g. a push that failed last session).</summary>
    public bool HasUnpushedStudioCommits()
    {
        var r = Git.Run(repo, ["rev-list", "--count", "--grep=^Thumbnail Studio: reframe", "origin/main..HEAD"]);
        return r.Ok && int.TryParse(r.Text.Trim(), out int n) && n > 0;
    }

    static string LastLine(string s)
    {
        var lines = s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "(no output)" : lines[^1];
    }
}

/// <summary>Runs git with the repo as working directory, no window, no prompts, bytes in and out.</summary>
public static class Git
{
    public sealed record Result(int Code, byte[] Stdout, string Error)
    {
        public bool Ok => Code == 0;
        public string Text => Encoding.UTF8.GetString(Stdout);
        public Result Check(string what) => Ok ? this : throw new InvalidOperationException($"git {what} failed: {Error.Trim()}");
    }

    public static Result Run(string repo, string[] args, byte[]? stdin = null, Dictionary<string, string>? env = null, TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=off");
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        psi.Environment.Remove("GIT_INDEX_FILE");
        if (env != null) foreach (var (k, v) in env) psi.Environment[k] = v;

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start git.");
        var outTask = Task.Run(() => { using var ms = new MemoryStream(); p.StandardOutput.BaseStream.CopyTo(ms); return ms.ToArray(); });
        var errTask = p.StandardError.ReadToEndAsync();
        if (stdin != null) p.StandardInput.BaseStream.Write(stdin);
        p.StandardInput.Close();
        if (!p.WaitForExit(timeout ?? TimeSpan.FromSeconds(30)))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            return new Result(-1, [], $"git {string.Join(' ', args)} timed out");
        }
        p.WaitForExit();
        return new Result(p.ExitCode, outTask.Result, errTask.Result);
    }
}

/// <summary>Appends to %LOCALAPPDATA%\MindAttic\ThumbnailStudio\studio.log.</summary>
public static class Log
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MindAttic", "ThumbnailStudio");
    static readonly object Lock = new();

    public static void Write(string line)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, "studio.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{Environment.ProcessId}] {line}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
