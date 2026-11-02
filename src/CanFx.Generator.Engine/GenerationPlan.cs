using System.Text;

namespace CanFx.Generator.Engine;

/// <summary>Üretimin sonucu: yazılacak dosyalar ve var olan dosyalara yapılacak eklemeler. <see cref="Apply"/> diske yazar.</summary>
public sealed class GenerationPlan
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly List<GeneratedFile> _files = [];
    private readonly List<Injection> _injections = [];
    private readonly List<string> _deletions = [];

    public IReadOnlyList<GeneratedFile> Files => _files;

    /// <summary>Var olan dosyalara yapılacak değişiklikler (dosya + açıklama).</summary>
    public IReadOnlyList<(string File, string Description)> Injections => _injections.Select(i => (i.File, i.Description)).ToList();

    /// <summary>Dosya ekler. C# dosyasında birden çok tip varsa her biri kendi dosyasına ayrılır (ör. istek + handler).</summary>
    public void AddFile(string path, string content)
    {
        foreach ((string file, string text) in TypeFileSplitter.Split(path, content.ReplaceLineEndings("\n")))
            _files.Add(new GeneratedFile(file, text) { Origin = path });
    }

    /// <summary>
    /// <paramref name="file"/>'da <c>// {marker}</c> satırının ÜSTÜNE <paramref name="line"/>'ı (işaretin girintisiyle)
    /// ekler; gerekirse <paramref name="usings"/>'i dosyanın başına yazar. Satır zaten varsa bir şey yapılmaz.
    /// </summary>
    public void Inject(string file, string marker, string line, params string[] usings) =>
        _injections.Add(new Injection(file, line.Trim().Split('\n')[0], text => SourceEditor.Inject(text, marker, line, usings)));

    /// <summary>Dosyada özel bir değişiklik (ör. JSON'a anahtar ekleme).</summary>
    internal void Edit(string file, string description, Func<string, InjectResult> edit) =>
        _injections.Add(new Injection(file, description, edit));

    /// <summary>Dosyayı siler (ör. bir mekanizma kapatılınca onun dosyaları).</summary>
    internal void Delete(string file) => _deletions.Add(file);

    /// <summary>Başka bir planın dosyalarını, değişikliklerini ve silmelerini bu plana katar.</summary>
    internal void Include(GenerationPlan other)
    {
        _files.AddRange(other._files);
        _injections.AddRange(other._injections);
        _deletions.AddRange(other._deletions);
    }

    /// <summary>
    /// Diske yazmadan değişikliklerin önizlemesi: her dosyanın önceki ve sonraki hali (yeni dosyada önceki, silinende
    /// sonraki <see langword="null"/>).
    /// </summary>
    public IReadOnlyList<FilePreview> Preview(bool force = false) => Compute(force).Select(c => c.Preview).ToList();

    /// <summary>Planı uygular. <paramref name="force"/> yoksa var olan dosyaların üzerine yazılmaz.</summary>
    public IReadOnlyList<ChangeResult> Apply(bool force = false, bool dryRun = false)
    {
        List<Computed> changes = Compute(force);
        if (!dryRun)
        {
            foreach (Computed change in changes)
            {
                switch (change.Result.Kind)
                {
                    case ChangeKind.Created or ChangeKind.Overwritten or ChangeKind.Updated:
                        Directory.CreateDirectory(Path.GetDirectoryName(change.Result.Path)!);
                        File.WriteAllText(change.Result.Path, change.Preview.After!, Utf8);
                        break;
                    case ChangeKind.Deleted:
                        File.Delete(change.Result.Path);
                        DeleteEmptyDirectory(Path.GetDirectoryName(change.Result.Path));
                        break;
                }
            }
        }

        return changes.Select(c => c.Result).ToList();
    }

    private sealed record Computed(ChangeResult Result, FilePreview Preview);

    private List<Computed> Compute(bool force)
    {
        var results = new List<Computed>();

        // Planın başındaki durum: yazılan dosyalar sonraki kontrolleri etkilemesin.
        var originsBefore = _files.Where(f => f.Origin != f.Path).Select(f => f.Origin).Distinct().Where(File.Exists).ToHashSet();

        foreach (GeneratedFile file in _files)
        {
            bool exists = File.Exists(file.Path);
            string? before = exists ? File.ReadAllText(file.Path) : null;

            // Ayrılan dosyalar (ör. handler) asıl dosyayla birlikte yazılır: asıl dosya korunuyorsa (eski üretimde handler
            // onun içinde olabilir) ayrılanlar da yazılmaz, yoksa aynı tip iki kez tanımlanır.
            if (!force && file.Origin != file.Path && originsBefore.Contains(file.Origin))
            {
                results.Add(Unchanged(file.Path, before, ChangeKind.Skipped, $"{System.IO.Path.GetFileName(file.Origin)} var, değiştirilmedi"));
                continue;
            }

            if (exists && !force)
            {
                results.Add(Unchanged(file.Path, before, ChangeKind.Skipped, "dosya var, değiştirilmedi"));
                continue;
            }

            results.Add(new Computed(
                new ChangeResult(file.Path, exists ? ChangeKind.Overwritten : ChangeKind.Created, null),
                new FilePreview(file.Path, exists ? ChangeKind.Overwritten : ChangeKind.Created, before, file.Content)));
        }

        foreach (IGrouping<string, Injection> group in _injections.GroupBy(i => i.File))
        {
            if (!File.Exists(group.Key))
            {
                results.Add(Unchanged(group.Key, null, ChangeKind.Failed, "dosya bulunamadı"));
                continue;
            }

            string original = File.ReadAllText(group.Key);
            string text = original;
            var notes = new List<string>();

            foreach (Injection injection in group)
            {
                InjectResult result = injection.Apply(text);
                text = result.Text;
                if (result.Note is not null)
                    notes.Add(result.Note);
            }

            ChangeKind kind = notes.Count > 0 && text == original ? ChangeKind.Failed : text == original ? ChangeKind.Unchanged : ChangeKind.Updated;
            string? note = notes.Count > 0 ? string.Join("; ", notes) : null;
            results.Add(new Computed(new ChangeResult(group.Key, kind, note), new FilePreview(group.Key, kind, original, text)));
        }

        foreach (string file in _deletions.Distinct(StringComparer.Ordinal))
        {
            if (File.Exists(file))
                results.Add(new Computed(new ChangeResult(file, ChangeKind.Deleted, null), new FilePreview(file, ChangeKind.Deleted, File.ReadAllText(file), null)));
        }

        return results;

        static Computed Unchanged(string path, string? text, ChangeKind kind, string note) =>
            new(new ChangeResult(path, kind, note), new FilePreview(path, kind, text, text));
    }

    private static void DeleteEmptyDirectory(string? directory)
    {
        if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }
}

/// <summary>Önizlemede bir dosya: önceki ve sonraki hali.</summary>
public sealed record FilePreview(string Path, ChangeKind Kind, string? Before, string? After);

public sealed record GeneratedFile(string Path, string Content)
{
    /// <summary>Dosyanın ayrıldığı asıl dosya (ayrılmadıysa kendisi).</summary>
    public string Origin { get; init; } = Path;
}

internal sealed record Injection(string File, string Description, Func<string, InjectResult> Apply);

public enum ChangeKind
{
    Created,
    Overwritten,
    Updated,
    Unchanged,
    Skipped,
    Failed,
    Deleted,
}

public sealed record ChangeResult(string Path, ChangeKind Kind, string? Note);

internal sealed record InjectResult(string Text, string? Note);

/// <summary>
/// İşaret adları. 0.1.0 şablonuyla oluşturulmuş çözümlerde işaretler eski adla (<c>// can-gen:...</c>) durur; ikisi de tanınır.
/// </summary>
internal static class Markers
{
    private const string Current = "canfxgen:";
    private const string Old = "can-gen:";

    /// <summary><c>canfxgen:dbsets</c> → <c>can-gen:dbsets</c>.</summary>
    public static string Legacy(string marker) =>
        marker.StartsWith(Current, StringComparison.Ordinal) ? Old + marker[Current.Length..] : marker;
}

/// <summary>İşaretli satırlara (<c>// canfxgen:...</c>) ekleme ve using ekleme.</summary>
internal static class SourceEditor
{
    /// <summary>
    /// JSON nesnesinin köküne <c>"{group}": { "{key}": "{value}" }</c> ekler (grup varsa dokunmaz). Yorumlu JSON'u bozmamak
    /// için metin olarak düzenlenir.
    /// </summary>
    public static InjectResult AddJsonGroup(string text, string group, string key, string value)
    {
        if (text.Contains($"\"{group}\"", StringComparison.Ordinal))
            return new InjectResult(text, null);

        int end = text.LastIndexOf('}');
        if (end < 0)
            return new InjectResult(text, "JSON nesnesi bulunamadı");

        int previous = end - 1;
        while (previous >= 0 && char.IsWhiteSpace(text[previous]))
            previous--;

        string separator = previous >= 0 && text[previous] != '{' ? "," : string.Empty;
        string entry = $"{separator}\n  \"{group}\": {{ \"{key}\": \"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\" }}\n";
        return new InjectResult(text[..(previous + 1)] + entry + text[end..], null);
    }

    public static InjectResult Inject(string text, string marker, string line, IReadOnlyList<string> usings)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        List<string> lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        string markerText = "// " + marker;
        string[] markerTexts = [markerText, "// " + Markers.Legacy(marker)];
        int index = lines.FindIndex(l => markerTexts.Any(m => l.Trim() == m || l.TrimEnd().EndsWith(m, StringComparison.Ordinal) && !l.TrimStart().StartsWith("///", StringComparison.Ordinal) && l.Trim().StartsWith("//", StringComparison.Ordinal)));
        if (index < 0)
            return new InjectResult(text, $"'{markerText}' işareti bulunamadı; şu satırı elle ekle: {line.Trim()}");

        // Çok satırlı ekleme (ör. controller metodu): satırlar işaretin girintisiyle, kendi göreli girintileri korunarak eklenir.
        string[] block = line.Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\n').Split('\n');
        string first = block.First(l => l.Trim().Length > 0).Trim();
        if (!lines.Any(l => l.Trim() == first))
        {
            string indent = lines[index][..(lines[index].Length - lines[index].TrimStart().Length)];
            int common = block.Where(l => l.Trim().Length > 0).Min(l => l.Length - l.TrimStart().Length);
            List<string> inserted = block.Select(l => l.Trim().Length == 0 ? string.Empty : indent + l[common..].TrimEnd()).ToList();
            if (inserted.Count > 1)
                inserted.Add(string.Empty); // çok satırlı bloktan (metot) sonra boş satır
            lines.InsertRange(index, inserted);
        }

        foreach (string @using in usings)
            AddUsing(lines, @using);

        return new InjectResult(string.Join(newline, lines), null);
    }

    private static void AddUsing(List<string> lines, string @namespace)
    {
        string statement = $"using {@namespace};";
        if (lines.Any(l => l.Trim() == statement))
            return;

        // Dosya kapsamlı namespace'ten önceki son using satırının altına (yoksa en üste).
        int namespaceLine = lines.FindIndex(l => l.TrimStart().StartsWith("namespace ", StringComparison.Ordinal));
        int limit = namespaceLine < 0 ? lines.Count : namespaceLine;
        int last = lines.FindLastIndex(limit - 1 < 0 ? 0 : limit - 1, limit, l => l.TrimStart().StartsWith("using ", StringComparison.Ordinal) && l.TrimEnd().EndsWith(';'));
        lines.Insert(last + 1, statement);
    }
}
