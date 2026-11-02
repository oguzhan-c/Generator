using System.Text.Json;

namespace CanFx.Generator.Engine;

/// <summary>Çözümdeki çeviri dosyalarını okur (diller, bilinen alan adları).</summary>
internal static class LocalizationFiles
{
    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary><c>fields.tr.json</c> → <c>tr</c>; <c>tr.json</c> → <c>tr</c>.</summary>
    public static string Culture(string path)
    {
        string[] parts = Path.GetFileNameWithoutExtension(path).Split('.');
        return parts[^1].ToLowerInvariant();
    }

    /// <summary>Klasördeki çeviri dosyalarının dilleri; klasör yoksa ya da boşsa <c>tr</c>, <c>en</c>.</summary>
    public static IReadOnlyList<string> Cultures(string directory)
    {
        List<string> cultures = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.json").Select(Culture).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()
            : [];
        return cultures.Count > 0 ? cultures : ["tr", "en"];
    }

    /// <summary>Ortak dosyada çevirisi olan alan adları (<c>"fields": { "Name": ... }</c>).</summary>
    public static IReadOnlySet<string> FieldNames(string path)
    {
        if (!File.Exists(path))
            return new HashSet<string>();

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), Options);
            return document.RootElement.TryGetProperty("fields", out JsonElement fields) && fields.ValueKind == JsonValueKind.Object
                ? fields.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>();
        }
        catch (JsonException)
        {
            return new HashSet<string>();
        }
    }
}
