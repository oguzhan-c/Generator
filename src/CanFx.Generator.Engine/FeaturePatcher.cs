using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CanFx.Generator.Engine;

/// <summary>
/// Şablon özelliğini sonradan açar / kapatır. Kurallar elle yazılmaz: şablon, projenin seçenekleriyle özellik kapalı
/// (taban) ve açık (hedef) olarak iki kez üretilir; aradaki fark (yeni / silinen dosyalar, değişen satırlar) projeye yama
/// olarak uygulanır. Böylece şablon değiştikçe de güncel kalır. Yama, projede değişmiş yerlere bağlamla (çevre satırlar)
/// yerleştirilir; yerleştirilemeyen parça elle eklenmek üzere raporlanır.
/// </summary>
public static partial class FeaturePatcher
{
    /// <summary>Sonradan açılıp kapatılabilen özellikler (şablon seçenek adları).</summary>
    public static readonly IReadOnlyList<string> Supported = ["Redis", "Search", "EventBus", "Hangfire", "FileStorage", "Realtime", "Sms", "OpenTelemetry", "Postman"];

    /// <summary>Şema ya da kimlik akışını değiştirdiği için sonradan eklenmeyenler.</summary>
    public static readonly IReadOnlyList<string> Unsupported = ["MultiTenancy", "Otp", "EmailTwoFactor", "Passkey", "ExternalLogin", "AuditTrail", "Settings", "Reporting", "Database", "IdKeyType"];

    private const string OptionsFile = "canfx.json";
    private const int Context = 3;

    /// <summary>Komut satırındaki ad → şablon seçenek adı (<c>redis</c> → <c>Redis</c>, <c>event-bus</c> → <c>EventBus</c>).</summary>
    public static string Normalize(string feature)
    {
        string compact = feature.Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
        string? match = Supported.Concat(Unsupported).FirstOrDefault(f => string.Equals(f, compact, StringComparison.OrdinalIgnoreCase));
        return compact switch
        {
            _ when match is not null => match,
            "signalr" => "Realtime",
            "elasticsearch" or "elastic" => "Search",
            "rabbitmq" or "bus" => "EventBus",
            "storage" or "files" or "s3" => "FileStorage",
            "otel" or "telemetry" => "OpenTelemetry",
            _ => throw new GeneratorException($"Bilinmeyen özellik: '{feature}'. Desteklenenler: {string.Join(", ", Supported.Select(s => s.ToLowerInvariant()))}."),
        };
    }

    // ------------------------------------------------------------------ seçenekler

    /// <summary>
    /// Projenin şablon seçenekleri: <c>canfx.json</c>'dan; yoksa (eski projeler) proje dosyalarından çıkarılır.
    /// </summary>
    public static Dictionary<string, string> ReadOptions(SolutionLayout layout, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(layout);
        string file = Path.Combine(layout.Root, OptionsFile);
        if (File.Exists(file))
        {
            JsonNode? options = JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["options"];
            if (options is JsonObject values)
                return values.ToDictionary(p => p.Key, p => p.Value?.ToString().ToLowerInvariant() ?? string.Empty, StringComparer.Ordinal);
        }

        warnings.Add($"{OptionsFile} yok (eski proje): seçenekler proje dosyalarından çıkarıldı.");
        return InferOptions(layout);
    }

    private static Dictionary<string, string> InferOptions(SolutionLayout layout)
    {
        static string ReadIf(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        string packages = ReadIf(Path.Combine(layout.Root, "Directory.Packages.props"));
        string persistence = ReadIf(Path.Combine(layout.Persistence, Path.GetFileName(layout.Persistence) + ".csproj"));
        string props = ReadIf(Path.Combine(layout.Root, "Directory.Build.props"));
        string account = ReadIf(Path.Combine(layout.WebApi, "Controllers", "AccountController.cs"));
        string registration = ReadIf(Path.Combine(layout.Persistence, "PersistenceServiceRegistration.cs"));
        bool Package(string id) => packages.Contains($"\"{id}\"", StringComparison.Ordinal);
        string Flag(bool value) => value ? "true" : "false";

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Database"] = persistence.Contains("SqlServer", StringComparison.Ordinal) ? "sqlserver" : persistence.Contains("Sqlite", StringComparison.Ordinal) ? "sqlite" : "postgresql",
            ["IdKeyType"] = props.Contains("System.Int32", StringComparison.Ordinal) ? "int" : props.Contains("System.Int64", StringComparison.Ordinal) ? "long" : "guid",
            ["MultiTenancy"] = Flag(File.Exists(Path.Combine(layout.Persistence, "Repositories", "CrossTenantReader.cs"))),
            ["Otp"] = Flag(account.Contains("EnableOtp", StringComparison.Ordinal)),
            ["EmailTwoFactor"] = Flag(account.Contains("EnableEmailTwoFactor", StringComparison.Ordinal)),
            ["Passkey"] = Flag(File.Exists(Path.Combine(layout.WebApi, "Security", "PasskeyCeremonyStore.cs"))),
            ["ExternalLogin"] = Flag(File.Exists(Path.Combine(layout.WebApi, "Security", "ExternalLoginCallback.cs"))),
            ["Redis"] = Flag(Package("CanFx.Caching.Redis")),
            ["EventBus"] = Flag(Package("CanFx.EventBus")),
            ["Hangfire"] = Flag(Package("CanFx.BackgroundJobs.Hangfire")),
            ["FileStorage"] = Flag(Package("CanFx.FileStorage")),
            ["Search"] = Flag(Package("CanFx.Search")),
            ["Realtime"] = Flag(Package("CanFx.Realtime")),
            ["Sms"] = Flag(Package("CanFx.Sms")),
            ["AuditTrail"] = Flag(registration.Contains("AddCanAuditTrail", StringComparison.Ordinal)),
            ["Settings"] = Flag(Package("CanFx.Settings")),
            ["Reporting"] = Flag(Package("CanFx.Reporting")),
            ["OpenTelemetry"] = Flag(Package("CanFx.Observability.OpenTelemetry")),
            ["Postman"] = Flag(Package("CanFx.WebApi.Postman")),
        };
    }

    /// <summary><c>dotnet new canfx-monolith</c> argümanları (seçeneklerden).</summary>
    public static IReadOnlyList<string> TemplateArguments(IReadOnlyDictionary<string, string> options) =>
        options.SelectMany(o => new[] { "--" + o.Key, o.Value }).ToList();

    // ------------------------------------------------------------------ plan

    /// <param name="layout">Proje.</param>
    /// <param name="baseDirectory">Şablonun projenin şu anki seçenekleriyle üretildiği klasör.</param>
    /// <param name="targetDirectory">Aynı seçeneklerle, yalnızca özellik değiştirilerek üretildiği klasör.</param>
    /// <param name="feature">Şablon seçenek adı.</param>
    /// <param name="enable">Açılıyor mu.</param>
    /// <param name="warnings">Elle yapılması gerekenler.</param>
    public static GenerationPlan Plan(SolutionLayout layout, string baseDirectory, string targetDirectory, string feature, bool enable, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(warnings);

        var plan = new GenerationPlan();
        HashSet<string> baseFiles = Files(baseDirectory);
        HashSet<string> targetFiles = Files(targetDirectory);

        foreach (string relative in targetFiles.Union(baseFiles).Order(StringComparer.Ordinal))
        {
            if (relative is OptionsFile or "README.md" || relative.EndsWith(".slnx", StringComparison.Ordinal))
                continue;

            string project = Path.Combine(layout.Root, relative);
            bool inBase = baseFiles.Contains(relative);
            bool inTarget = targetFiles.Contains(relative);

            if (inTarget && !inBase)
            {
                plan.AddFile(project, File.ReadAllText(Path.Combine(targetDirectory, relative)));
            }
            else if (inBase && !inTarget)
            {
                if (!File.Exists(project))
                    continue;
                if (Same(File.ReadAllText(project), File.ReadAllText(Path.Combine(baseDirectory, relative))))
                    plan.Delete(project);
                else
                    warnings.Add($"{relative} değiştirilmiş olduğu için silinmedi; özellik kapandığından artık gerekmiyorsa sil.");
            }
            else
            {
                string before = File.ReadAllText(Path.Combine(baseDirectory, relative));
                string after = SameGeneratedValues(before, File.ReadAllText(Path.Combine(targetDirectory, relative)));
                if (before == after)
                    continue;

                if (!File.Exists(project))
                {
                    warnings.Add($"{relative} projede yok; özellik bu dosyada değişiklik istiyor, atlandı.");
                    continue;
                }

                List<Hunk> hunks = Hunks(Lines(before), Lines(after));
                plan.Edit(project, $"{feature} {(enable ? "eklendi" : "çıkarıldı")}", text =>
                {
                    (string patched, List<string> failed) = ApplyHunks(text, hunks, Lines(before).Length);
                    foreach (string failure in failed)
                        warnings.Add($"{relative}: elle yap → {failure}");
                    return new InjectResult(patched, failed.Count > 0 && patched == text ? $"{failed.Count} değişiklik yerleştirilemedi" : null);
                });
            }
        }

        string options = Path.Combine(layout.Root, OptionsFile);
        if (File.Exists(options))
        {
            plan.Edit(options, $"{feature}: {(enable ? "true" : "false")}", text =>
                new InjectResult(Regex.Replace(text, $"(\"{feature}\"\\s*:\\s*)(true|false)", $"${{1}}{(enable ? "true" : "false")}"), null));
        }
        else if (File.Exists(Path.Combine(targetDirectory, OptionsFile)))
        {
            plan.AddFile(options, File.ReadAllText(Path.Combine(targetDirectory, OptionsFile)));
        }

        return plan;
    }

    private static HashSet<string> Files(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .Where(f => !f.Split('/').Any(part => part is "bin" or "obj" or ".git" or ".idea" or ".vs"))
                .ToHashSet(StringComparer.Ordinal)
            : [];

    /// <summary>Her üretimde farklı olan değerler (user-secrets kimliği) farktan sayılmasın.</summary>
    private static string SameGeneratedValues(string before, string after)
    {
        Match secrets = UserSecrets().Match(before);
        return secrets.Success ? UserSecrets().Replace(after, secrets.Value) : after;
    }

    private static bool Same(string a, string b) =>
        string.Join('\n', Lines(a).Select(l => l.Trim())) == string.Join('\n', Lines(b).Select(l => l.Trim()));

    private static string[] Lines(string text) => text.ReplaceLineEndings("\n").Split('\n');

    // ------------------------------------------------------------------ yama

    internal sealed record Hunk(string[] Before, string[] Removed, string[] Added, string[] After, int Index);

    /// <summary>
    /// Taban → hedef satır farkı, değişiklik bloğu başına bir parça. Parçalar sırayla uygulanır: öndeki bağlam önceki
    /// parçalar uygulanmış haldeki satırlardan (aynı + eklenen), arkadaki bağlam henüz değişmemiş satırlardan (aynı +
    /// silinecek) alınır.
    /// </summary>
    internal static List<Hunk> Hunks(string[] a, string[] b)
    {
        int[,] lcs = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
        {
            for (int j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
        }

        // Düzenleme dizisi: ' ' aynı, '-' yalnızca tabanda, '+' yalnızca hedefte
        var ops = new List<(char Kind, string Text)>();
        int x = 0, y = 0;
        while (x < a.Length || y < b.Length)
        {
            if (x < a.Length && y < b.Length && a[x] == b[y])
            {
                ops.Add((' ', a[x]));
                x++;
                y++;
            }
            else if (y < b.Length && (x == a.Length || lcs[x, y + 1] >= lcs[x + 1, y]))
            {
                ops.Add(('+', b[y++]));
            }
            else
            {
                ops.Add(('-', a[x++]));
            }
        }

        var hunks = new List<Hunk>();
        int k = 0;
        while (k < ops.Count)
        {
            if (ops[k].Kind == ' ')
            {
                k++;
                continue;
            }

            int end = k;
            while (end + 1 < ops.Count && ops[end + 1].Kind != ' ')
                end++;

            string[] before = ops.Take(k).Where(o => o.Kind != '-').Select(o => o.Text).TakeLast(Context).ToArray();
            string[] after = ops.Skip(end + 1).Where(o => o.Kind != '+').Select(o => o.Text).Take(Context).ToArray();
            string[] removed = ops.Skip(k).Take(end - k + 1).Where(o => o.Kind == '-').Select(o => o.Text).ToArray();
            string[] added = ops.Skip(k).Take(end - k + 1).Where(o => o.Kind == '+').Select(o => o.Text).ToArray();
            int index = ops.Take(k).Count(o => o.Kind != '-');
            hunks.Add(new Hunk(before, removed, added, after, index));
            k = end + 1;
        }

        return hunks;
    }

    /// <summary>
    /// Parçaları sırayla uygular. Her parça bağlamıyla aranır (boşluklar yok sayılır; önce tam bağlam, bulunamazsa daha
    /// kısası); birden çok yerde bulunursa beklenen konuma en yakını seçilir. Zaten uygulanmış parça atlanır.
    /// </summary>
    internal static (string Text, List<string> Failed) ApplyHunks(string text, IReadOnlyList<Hunk> hunks, int baseLength)
    {
        var lines = Lines(text).ToList();
        var failed = new List<string>();

        foreach (Hunk hunk in hunks)
        {
            // Zaten uygulanmış mı (hedef hali bağlamıyla var)?
            if (hunk.Added.Any(l => l.Trim().Length > 0)
                && Attempts(hunk, hunk.Added).Any(t => Find(lines, Pattern(hunk, hunk.Added, t), hunk.Index, t.Before) >= 0))
            {
                continue;
            }

            int position = -1, before = 0;
            foreach ((int b, int f) attempt in Attempts(hunk, hunk.Removed))
            {
                position = Find(lines, Pattern(hunk, hunk.Removed, attempt), hunk.Index, attempt.b);
                if (position >= 0)
                {
                    before = attempt.b;
                    break;
                }
            }

            if (position < 0)
            {
                // Silinecekler yok, eklenecekler var: zaten uygulanmış (bağlam değişmiş olsa da).
                var present = lines.Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);
                bool removedGone = hunk.Removed.Where(l => l.Trim().Length > 0).All(l => !present.Contains(l.Trim()));
                bool addedThere = hunk.Added.Where(l => l.Trim().Length > 0).All(l => present.Contains(l.Trim()));
                if (removedGone && addedThere)
                    continue;

                failed.Add(Describe(hunk));
                continue;
            }

            int at = position + before;
            lines.RemoveRange(at, hunk.Removed.Length);
            lines.InsertRange(at, hunk.Added);
        }

        return (string.Join('\n', lines), failed);
    }

    private static string[] Pattern(Hunk hunk, string[] middle, (int Before, int After) context) =>
        [.. hunk.Before[(hunk.Before.Length - context.Before)..], .. middle, .. hunk.After[..context.After]];

    /// <summary>
    /// Bağlam denemeleri: tam → kısalan. Bağlamsız arama yalnızca ortadaki satırlar ayırt ediciyse (tek "}" her yerde
    /// var); ortası boşsa en az bir yanda bağlam gerekir.
    /// </summary>
    private static IEnumerable<(int Before, int After)> Attempts(Hunk hunk, string[] middle)
    {
        int b = hunk.Before.Length, f = hunk.After.Length;
        var seen = new HashSet<(int, int)>();
        foreach ((int x, int y) in new[] { (b, f), (b, Math.Min(f, 1)), (Math.Min(b, 1), f), (Math.Min(b, 2), Math.Min(f, 2)), (Math.Min(b, 1), Math.Min(f, 1)), (b, 0), (0, f), (Math.Min(b, 1), 0), (0, Math.Min(f, 1)), (0, 0) })
        {
            if (x + y == 0 && !middle.Any(l => l.Trim().Length >= 12))
                continue;
            if (Pattern(hunk, middle, (x, y)).All(string.IsNullOrWhiteSpace))
                continue;
            if (seen.Add((x, y)))
                yield return (x, y);
        }
    }

    /// <summary>Deseni (boşluklar yok sayılarak) arar; birden çok eşleşmede beklenen konuma en yakını.</summary>
    private static int Find(List<string> lines, string[] pattern, int expected, int before)
    {
        if (pattern.Length == 0 || pattern.Length > lines.Count)
            return -1;

        int best = -1;
        for (int i = 0; i <= lines.Count - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length && match; j++)
                match = lines[i + j].Trim() == pattern[j].Trim();

            if (match && (best < 0 || Math.Abs(i + before - expected) < Math.Abs(best + before - expected)))
                best = i;
        }

        return best;
    }

    private static string Describe(Hunk hunk)
    {
        string anchor = hunk.Before.LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? hunk.After.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "dosya başı";
        static string Mark(string[] items, string mark) => string.Join(string.Empty, items.Where(l => l.Trim().Length > 0).Select(l => $"\n      {mark} {l.Trim()}"));
        return $"'{anchor}' satırından sonra:{Mark(hunk.Removed, "-")}{Mark(hunk.Added, "+")}";
    }

    [GeneratedRegex(@"<UserSecretsId>[^<]*</UserSecretsId>")]
    private static partial Regex UserSecrets();
}
