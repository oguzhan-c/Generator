using System.Text.RegularExpressions;

namespace CanFx.Generator.Engine;

/// <summary>
/// Dosya başına tek tip: üretilen bir C# dosyasında birden çok üst düzey tip varsa (ör. istek + handler) her biri
/// kendi dosyasına (<c>{Tip}.cs</c>, aynı klasör) yazılır. <c>using</c>'ler her dosyaya kopyalanır; kullanılmadığı
/// bilinenler çıkarılır.
/// </summary>
internal static partial class TypeFileSplitter
{
    /// <summary>Namespace → kullanıldığını gösteren adlar. Listede olmayan namespace'ler her dosyada kalır.</summary>
    private static readonly (Regex Namespace, Regex Usage)[] KnownUsings =
    [
        (Exact("System.Text.Json.Serialization"), Words("JsonIgnore", "JsonPropertyName", "JsonConverter")),
        (Exact("CanFx.Mediator"), Words("IRequest", "IRequestHandler", "INotification", "INotificationHandler", "ISender", "IPublisher")),
        (Exact("CanFx.Mapping"), Words("IMapper", "ProjectTo", "MappingProfile")),
        (Exact("CanFx.Application"), Words("ISecuredRequest", "ILoggableRequest", "ITransactionalRequest", "ICachableRequest", "ICacheRemoverRequest", "OrThrow", "CacheTags")),
        (Exact("CanFx.Application.Requests"), Words("PageRequest")),
        (Exact("CanFx.Application.Responses"), Words("GetListResponse", "ToGetListResponse")),
        (Exact("CanFx.Persistence.Repositories"), Words("IRepository", "IUnitOfWork", "IAsyncRepository")),
        (Exact("CanFx.Persistence.Paging"), Words("IPaginate", "ToPaginateAsync", "Paginate")),
        (Exact("CanFx.Persistence.Dynamic"), Words("DynamicQuery", "DynamicQueryFields", "GetListByDynamicAsync")),
        (new Regex(@"\.Application\.Services\.Repositories$"), new Regex(@"\bI\w+Repository\b")),
        (new Regex(@"\.Features\.\w+\.Rules$"), new Regex(@"\b\w+BusinessRules\b")),
        (new Regex(@"\.Features\.\w+\.Constants$"), new Regex(@"\b\w+(OperationClaims|BusinessMessages)\b")),
    ];

    public static IEnumerable<(string Path, string Content)> Split(string path, string content)
    {
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return [(path, content)];

        string[] lines = content.Split('\n');
        var starts = new List<(int Line, string Name)>();
        for (int i = 0; i < lines.Length; i++)
        {
            Match m = Declaration().Match(lines[i]);
            if (m.Success)
                starts.Add((i, m.Groups["name"].Value));
        }

        int ns = Array.FindIndex(lines, l => l.StartsWith("namespace ", StringComparison.Ordinal));
        if (starts.Count < 2 || ns < 0 || starts[0].Line < ns)
            return [(path, content)];

        // Tipin başı: üstündeki XML yorumu ve attribute satırları da ona aittir.
        var blockStarts = starts.Select(s =>
        {
            int j = s.Line;
            while (j - 1 > ns && (lines[j - 1].StartsWith("///", StringComparison.Ordinal) || lines[j - 1].StartsWith('[')))
                j--;
            return j;
        }).ToList();

        string[] header = lines[..ns];
        string namespaceLine = lines[ns];
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string fileName = Path.GetFileNameWithoutExtension(path);
        var results = new List<(string, string)>();

        for (int k = 0; k < starts.Count; k++)
        {
            int end = k + 1 < blockStarts.Count ? blockStarts[k + 1] : lines.Length;
            string body = string.Join('\n', lines[blockStarts[k]..end]).TrimEnd();
            string usings = string.Join('\n', Prune(header, body)).TrimEnd();
            string name = starts[k].Name;
            string target = name == fileName ? path : Path.Combine(directory, name + ".cs");
            results.Add((target, $"{usings}\n\n{namespaceLine}\n\n{body}\n"));
        }

        return results;
    }

    private static IEnumerable<string> Prune(string[] header, string body)
    {
        foreach (string line in header)
        {
            Match m = UsingLine().Match(line);
            if (m.Success)
            {
                string ns = m.Groups["ns"].Value;
                Regex? usage = KnownUsings.Where(u => u.Namespace.IsMatch(ns)).Select(u => u.Usage).FirstOrDefault();
                if (usage is not null && !usage.IsMatch(body))
                    continue;
            }

            yield return line;
        }
    }

    private static Regex Exact(string ns) => new("^" + Regex.Escape(ns) + "$");

    private static Regex Words(params string[] words) => new(@"\b(" + string.Join('|', words) + @")\b");

    [GeneratedRegex(@"^(?:public|internal)\s+(?:(?:sealed|static|abstract|partial|readonly|file)\s+)*(?:class|record|interface|enum|struct)\s+(?<name>\w+)")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^using\s+(?<ns>[\w.]+)\s*;\s*$")]
    private static partial Regex UsingLine();
}
