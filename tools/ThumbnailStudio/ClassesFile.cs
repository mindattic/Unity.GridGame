using System.IO;
using System.Text;
using System.Text.Json;

namespace ThumbnailStudio;

/// <summary>What the studio needs from one class object in classes.json.</summary>
public sealed record ClassInfo(string Id, string Name, string Team, bool IsBoss, Thumb? Thumbnail);

/// <summary>
/// Reads classes.json, and writes one class's "Thumbnail" back with a targeted byte edit so every other byte
/// of the file stays identical (other agents and tools own the rest of that file).
/// </summary>
public static class ClassesFile
{
    static readonly JsonReaderOptions ReaderOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static List<ClassInfo> Load(string path) => Parse(File.ReadAllBytes(path));

    static List<ClassInfo> Parse(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes.AsMemory(BomLength(bytes)), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        var list = new List<ClassInfo>();
        foreach (var c in doc.RootElement.EnumerateArray())
        {
            if (c.ValueKind != JsonValueKind.Object || !c.TryGetProperty("Id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                continue;
            string id = idEl.GetString()!;
            string name = c.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : id;
            string team = c.TryGetProperty("Team", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
            bool boss = false;
            if (c.TryGetProperty("Tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                boss = tags.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "Boss");
            if (c.TryGetProperty("Footprint", out var fp) && fp.ValueKind == JsonValueKind.Object)
                boss |= Int(fp, "W") > 1 || Int(fp, "H") > 1;
            list.Add(new ClassInfo(id, name, team, boss, ReadThumb(c)));
        }
        return list;
    }

    static int Int(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 1;

    static Thumb? ReadThumb(JsonElement c)
    {
        if (!c.TryGetProperty("Thumbnail", out var t) || t.ValueKind != JsonValueKind.Object) return null;
        double D(string k, double d) => t.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : d;
        return new Thumb(D("Zoom", Thumb.Default.Zoom), D("X", Thumb.Default.X), D("Y", Thumb.Default.Y)).Clamped();
    }

    static int BomLength(byte[] b) => b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;

    /// <summary>Byte offsets (relative to the start of the JSON, after any BOM) inside the class object.</summary>
    sealed class Spans
    {
        public int ThumbPropStart = -1, ThumbValueStart = -1, ThumbValueEnd = -1;
        public int PortraitPropStart = -1, PortraitValueEnd = -1;
        public int IdPropStart = -1, IdValueEnd = -1;
    }

    /// <summary>
    /// Writes <paramref name="thumb"/> as the "Thumbnail" of class <paramref name="id"/>. The file is re-read
    /// first, so edits other tools made since loading are kept. Only that one block changes: an existing
    /// Thumbnail value is replaced in place (keeping its single-line / multi-line style), or a new block is
    /// inserted on the line after "Portrait" (else after "Id"), matching that property's indentation and the
    /// file's line endings.
    /// </summary>
    public static void SaveThumbnail(string path, string id, Thumb thumb)
    {
        var bytes = File.ReadAllBytes(path);
        int bom = BomLength(bytes);
        var json = bytes.AsSpan(bom);
        var s = Find(json, id) ?? throw new InvalidOperationException($"Class \"{id}\" was not found in {path}.");
        string nl = DetectNewline(json);
        thumb = thumb.Clamped();
        string z = Thumb.Num(thumb.Zoom), x = Thumb.Num(thumb.X), y = Thumb.Num(thumb.Y);

        int at, removeLen;
        string insert;
        if (s.ThumbValueStart >= 0)
        {
            at = s.ThumbValueStart;
            removeLen = s.ThumbValueEnd - s.ThumbValueStart;
            bool multiLine = json[s.ThumbValueStart..s.ThumbValueEnd].IndexOf((byte)'\n') >= 0;
            string ind = LineIndent(json, s.ThumbPropStart);
            insert = multiLine
                ? $"{{{nl}{ind}  \"Zoom\": {z},{nl}{ind}  \"X\": {x},{nl}{ind}  \"Y\": {y}{nl}{ind}}}"
                : $"{{ \"Zoom\": {z}, \"X\": {x}, \"Y\": {y} }}";
        }
        else
        {
            int propStart = s.PortraitPropStart >= 0 ? s.PortraitPropStart : s.IdPropStart;
            int valueEnd = s.PortraitPropStart >= 0 ? s.PortraitValueEnd : s.IdValueEnd;
            if (propStart < 0) throw new InvalidOperationException($"Class \"{id}\" has neither Portrait nor Id to anchor on.");
            string ind = LineIndent(json, propStart);
            // Is the anchor property followed by a comma (i.e. not the object's last property)?
            int k = valueEnd;
            while (k < json.Length && (json[k] == ' ' || json[k] == '\t')) k++;
            bool anchorHasComma = k < json.Length && json[k] == (byte)',';
            string block = $"{ind}\"Thumbnail\": {{{nl}{ind}  \"Zoom\": {z},{nl}{ind}  \"X\": {x},{nl}{ind}  \"Y\": {y}{nl}{ind}}}";
            if (anchorHasComma)
            {
                // Insert as its own line(s) right after the anchor line.
                int eol = json[k..].IndexOf((byte)'\n');
                if (eol < 0) throw new InvalidOperationException("Unexpected end of file after the anchor property.");
                at = k + eol + 1;
                removeLen = 0;
                insert = block + "," + nl;
            }
            else
            {
                // Anchor is the last property: give it a comma, then the block (which stays last, no comma).
                at = valueEnd;
                removeLen = 0;
                insert = "," + nl + block;
            }
        }

        var add = Encoding.UTF8.GetBytes(insert);
        var result = new byte[bytes.Length - removeLen + add.Length];
        int abs = bom + at;
        Buffer.BlockCopy(bytes, 0, result, 0, abs);
        Buffer.BlockCopy(add, 0, result, abs, add.Length);
        Buffer.BlockCopy(bytes, abs + removeLen, result, abs + add.Length, bytes.Length - abs - removeLen);

        // Never write a file that no longer parses or doesn't read back what we meant.
        var back = Parse(result).FirstOrDefault(c => c.Id == id)?.Thumbnail;
        if (back is null || back.Value.Rounded() != thumb.Rounded())
            throw new InvalidOperationException("The edited classes.json did not read back the saved Thumbnail; nothing was written.");

        // Write beside the file, then swap it in, so a crash mid-write can't leave a truncated classes.json.
        string tmp = path + ".thumbstudio.tmp";
        File.WriteAllBytes(tmp, result);
        File.Move(tmp, path, overwrite: true);
    }

    static string DetectNewline(ReadOnlySpan<byte> json)
    {
        int i = json.IndexOf((byte)'\n');
        return i > 0 && json[i - 1] == (byte)'\r' ? "\r\n" : "\n";
    }

    /// <summary>The run of spaces/tabs at the start of the line holding <paramref name="pos"/>.</summary>
    static string LineIndent(ReadOnlySpan<byte> json, int pos)
    {
        int start = pos;
        while (start > 0 && json[start - 1] != (byte)'\n') start--;
        int end = start;
        while (end < pos && (json[end] == ' ' || json[end] == '\t')) end++;
        return Encoding.UTF8.GetString(json[start..end]);
    }

    /// <summary>Walks the root array with a reader that reports byte offsets, and returns the spans of class <paramref name="id"/>.</summary>
    static Spans? Find(ReadOnlySpan<byte> json, string id)
    {
        var r = new Utf8JsonReader(json, ReaderOptions);
        Spans? cur = null;
        string? curId = null;
        while (r.Read())
        {
            if (r.TokenType == JsonTokenType.StartObject && r.CurrentDepth == 1)
            {
                cur = new Spans();
                curId = null;
                continue;
            }
            if (r.TokenType == JsonTokenType.EndObject && r.CurrentDepth == 1)
            {
                if (curId == id) return cur;
                cur = null;
                continue;
            }
            if (cur == null || r.TokenType != JsonTokenType.PropertyName || r.CurrentDepth != 2) continue;

            string name = r.GetString()!;
            int propStart = (int)r.TokenStartIndex;
            r.Read();
            int valueStart = (int)r.TokenStartIndex;
            string? str = r.TokenType == JsonTokenType.String ? r.GetString() : null;
            if (r.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) r.Skip();
            // r.BytesConsumed is the offset just past the current token (the value, or its closing bracket).
            int valueEnd = (int)r.BytesConsumed;

            switch (name)
            {
                case "Id":
                    curId = str;
                    cur.IdPropStart = propStart; cur.IdValueEnd = valueEnd;
                    break;
                case "Portrait":
                    cur.PortraitPropStart = propStart; cur.PortraitValueEnd = valueEnd;
                    break;
                case "Thumbnail":
                    cur.ThumbPropStart = propStart; cur.ThumbValueStart = valueStart; cur.ThumbValueEnd = valueEnd;
                    break;
            }
        }
        return null;
    }
}
