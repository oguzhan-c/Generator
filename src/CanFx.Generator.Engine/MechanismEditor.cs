using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CanFx.Generator.Engine;

/// <summary>Bir isteğe sonradan açılıp kapatılabilen mekanizmalar.</summary>
public enum Mechanism
{
    /// <summary>Sorgu sonucu önbellekte (ICachableRequest); komutta ilgili önbelleği siler (ICacheRemoverRequest).</summary>
    Caching,

    /// <summary>İstek loglanır (ILoggableRequest).</summary>
    Logging,

    /// <summary>Handler transaction içinde, sonunda kaydedilir (ITransactionalRequest).</summary>
    Transaction,

    /// <summary>Yetki kontrolü (ISecuredRequest + Permissions).</summary>
    Secured,

    /// <summary>Entity'nin dinamik filtre/sıralamalı listesi (GetListByDynamic; yalnızca entity / özellik için).</summary>
    DynamicQuery,
}

/// <summary>
/// Üretilmiş (ya da elle yazılmış) isteklerde mekanizmaları açar / kapatır: istek sınıfına arayüz ve üyelerini ekler ya
/// da çıkarır. Dosyanın geri kalanına (biçim, yorumlar, senin eklediklerin) dokunulmaz. Hedef: tek istek (ad ya da
/// dosya yolu), bir özellik (Features/{Ad}) ya da bir entity (özelliğinin tüm istekleri).
/// </summary>
public static partial class MechanismEditor
{
    private static readonly string[] CacheMembers = ["CacheKey", "CacheTags", "CacheExpiration", "BypassCache", "CacheTagsToRemove", "CacheKeysToRemove"];

    public static GenerationPlan Plan(SolutionLayout layout, string target, IReadOnlyCollection<Mechanism> mechanisms, bool enable, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(mechanisms);
        ArgumentNullException.ThrowIfNull(warnings);
        if (mechanisms.Count == 0)
            throw new GeneratorException("En az bir mekanizma gerekli: caching, logging, transaction, secured, dynamic-query.");

        (string? feature, EntityModel? entity, List<RequestFile> requests) = Resolve(layout, target);
        var plan = new GenerationPlan();

        foreach (Mechanism mechanism in mechanisms.Distinct())
        {
            if (mechanism == Mechanism.DynamicQuery)
            {
                DynamicQuery(layout, plan, feature, entity, enable, warnings);
                continue;
            }

            foreach (RequestFile request in requests)
            {
                if (mechanism == Mechanism.Caching && request.HasDynamicQuery && enable)
                {
                    warnings.Add($"{request.Name}: istemcinin gönderdiği filtreye göre değişen sorgu önbelleğe alınmaz (atlandı).");
                    continue;
                }

                if (mechanism == Mechanism.Transaction)
                    TransactionWarning(request, enable, warnings);

                RequestFile current = request;
                plan.Edit(request.Path, $"{request.Name}: {Name(mechanism)} {(enable ? "açıldı" : "kapatıldı")}",
                    text => Edit(layout, current, text, mechanism, enable));
            }
        }

        return plan;
    }

    /// <summary>Komut satırındaki ad → mekanizma.</summary>
    public static Mechanism? Parse(string name) => name.ToLowerInvariant() switch
    {
        "caching" or "cache" => Mechanism.Caching,
        "logging" or "log" => Mechanism.Logging,
        "transaction" or "tx" => Mechanism.Transaction,
        "secured" or "security" or "auth" => Mechanism.Secured,
        "dynamic-query" or "dynamic" => Mechanism.DynamicQuery,
        _ => null,
    };

    public static string Name(Mechanism mechanism) => mechanism switch
    {
        Mechanism.Caching => "önbellek",
        Mechanism.Logging => "log",
        Mechanism.Transaction => "transaction",
        Mechanism.Secured => "yetki",
        _ => "dinamik sorgu",
    };

    // ------------------------------------------------------------------ hedef

    private sealed record RequestFile(string Path, string Name, string Feature, bool IsCommand, bool HasDynamicQuery);

    private static (string? Feature, EntityModel? Entity, List<RequestFile> Requests) Resolve(SolutionLayout layout, string target)
    {
        string features = Path.Combine(layout.Application, "Features");
        List<RequestFile> all = Directory.Exists(features)
            ? SolutionLayout.SourceFiles(features).SelectMany(ReadRequests).ToList()
            : [];

        // Dosya yolu ya da istek adı: tek istek
        if (target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            string full = Path.GetFullPath(target);
            List<RequestFile> inFile = all.Where(r => string.Equals(r.Path, full, StringComparison.Ordinal)).ToList();
            return inFile.Count > 0 ? (inFile[0].Feature, null, inFile) : throw new GeneratorException($"Dosyada istek (IRequest) bulunamadı: {target}");
        }

        if (all.Where(r => r.Name == target).ToList() is { Count: > 0 } byName)
            return (byName[0].Feature, null, byName);

        // Özellik ya da entity: özelliğin tüm istekleri
        string? feature = Directory.Exists(Path.Combine(features, target)) ? target : null;
        EntityModel? entity = null;
        if (EntityReader.List(layout).FirstOrDefault(e => e.Name == target) is { Name: not null } found)
        {
            entity = EntityReader.Read(layout, found.FilePath);
            feature ??= Names.Plural(entity.Name);
        }
        else if (feature is not null && EntityReader.List(layout).FirstOrDefault(e => e.Name == Names.Singular(feature)) is { Name: not null } single)
        {
            entity = EntityReader.Read(layout, single.FilePath);
        }

        if (feature is null)
            throw new GeneratorException($"'{target}' bulunamadı: istek adı (CreateBrandCommand), özellik (Brands), entity (Brand) ya da dosya yolu ver.");

        return (feature, entity, all.Where(r => r.Feature == feature).ToList());
    }

    private static IEnumerable<RequestFile> ReadRequests(string path)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();
        string? feature = FeatureOf(root);
        foreach (ClassDeclarationSyntax cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (cls.BaseList is null || !cls.BaseList.Types.Any(t => BaseName(t) == "IRequest"))
                continue;

            bool dynamic = cls.Members.OfType<PropertyDeclarationSyntax>().Any(p => p.Type.ToString().TrimEnd('?') == "DynamicQuery");
            yield return new RequestFile(Path.GetFullPath(path), cls.Identifier.Text, feature ?? string.Empty, !cls.Identifier.Text.EndsWith("Query", StringComparison.Ordinal), dynamic);
        }
    }

    /// <summary><c>X.Application.Features.Brands.Commands.Create</c> → <c>Brands</c>.</summary>
    private static string? FeatureOf(CompilationUnitSyntax root)
    {
        string? ns = root.Members.OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
        int start = ns?.IndexOf(".Features.", StringComparison.Ordinal) ?? -1;
        return ns is null || start < 0 ? null : ns[(start + ".Features.".Length)..].Split('.')[0];
    }

    private static string BaseName(BaseTypeSyntax type) => type.Type switch
    {
        GenericNameSyntax generic => generic.Identifier.Text,
        QualifiedNameSyntax { Right: GenericNameSyntax generic } => generic.Identifier.Text,
        QualifiedNameSyntax qualified => qualified.Right.ToString(),
        var other => other.ToString(),
    };

    // ------------------------------------------------------------------ düzenleme

    private sealed record TextEdit(int Start, int Length, string Text);

    private static InjectResult Edit(SolutionLayout layout, RequestFile request, string text, Mechanism mechanism, bool enable)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();
        ClassDeclarationSyntax? cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c => c.Identifier.Text == request.Name);
        if (cls?.BaseList is null)
            return new InjectResult(text, $"{request.Name} sınıfı bulunamadı");
        if (cls.OpenBraceToken.IsMissing)
            return new InjectResult(text, $"{request.Name} gövdesiz tanımlanmış ('{{ }}' ekle)");

        string[] interfaces = mechanism switch
        {
            Mechanism.Caching => request.IsCommand ? ["ICacheRemoverRequest"] : ["ICachableRequest"],
            Mechanism.Logging => ["ILoggableRequest"],
            Mechanism.Transaction => ["ITransactionalRequest"],
            _ => ["ISecuredRequest"],
        };
        string[] members = mechanism switch
        {
            Mechanism.Caching => CacheMembers,
            Mechanism.Secured => ["Permissions", "Roles"],
            _ => [],
        };

        var edits = new List<TextEdit>();
        var usings = new List<string>();
        bool has = cls.BaseList.Types.Any(t => interfaces.Contains(BaseName(t)));

        if (enable)
        {
            if (has)
                return new InjectResult(text, null); // zaten açık

            BaseTypeSyntax last = cls.BaseList.Types.Last();
            edits.Add(new TextEdit(last.Span.End, 0, ", " + interfaces[0]));
            usings.Add("CanFx.Application");

            string? body = mechanism switch
            {
                Mechanism.Secured => SecuredMembers(layout, request, usings),
                Mechanism.Caching => CachingMembers(layout, request, cls, usings),
                _ => null,
            };
            if (body is not null && !cls.Members.OfType<PropertyDeclarationSyntax>().Any(p => members.Contains(p.Identifier.Text)))
                edits.Add(AddMembers(text, cls, body));
        }
        else
        {
            SeparatedSyntaxList<BaseTypeSyntax> types = cls.BaseList.Types;
            for (int i = 0; i < types.Count; i++)
            {
                if (!interfaces.Contains(BaseName(types[i])))
                    continue;

                // Önceki virgülle birlikte sil (ilk tip IRequest olduğundan i > 0).
                int start = i > 0 ? types.GetSeparator(i - 1).SpanStart : types[i].SpanStart;
                edits.Add(new TextEdit(start, types[i].Span.End - start, string.Empty));
            }

            foreach (PropertyDeclarationSyntax property in cls.Members.OfType<PropertyDeclarationSyntax>().Where(p => members.Contains(p.Identifier.Text)))
                edits.Add(RemoveMember(text, property));

            if (edits.Count == 0)
                return new InjectResult(text, null); // zaten kapalı
        }

        string result = Apply(text, edits);
        foreach (string ns in usings.Distinct())
            result = AddUsing(result, ns);
        return new InjectResult(Tidy(result), null);
    }

    private static string SecuredMembers(SolutionLayout layout, RequestFile request, List<string> usings)
    {
        string claims = $"{request.Feature}OperationClaims";
        string constants = Path.Combine(layout.Application, "Features", request.Feature, "Constants", claims + ".cs");
        string action = request.IsCommand ? "Write" : "Read";
        usings.Add("System.Text.Json.Serialization");

        if (!File.Exists(constants))
        {
            return $"""
                    // TODO(canfxgen): {claims} bulunamadı; gereken yetkileri yaz.
                    [JsonIgnore]
                    public IReadOnlyCollection<string> Permissions => [];
                """;
        }

        usings.Add($"{layout.RootNamespace}.Application.Features.{request.Feature}.Constants");
        string source = File.ReadAllText(constants);
        var list = new List<string>();
        foreach (string claim in new[] { "Admin", action })
        {
            if (Regex.IsMatch(source, $@"\bconst\s+string\s+{claim}\b"))
                list.Add($"{claims}.{claim}");
        }

        string todo = list.Count == 0 ? $"    // TODO(canfxgen): {claims}'ten gereken yetkileri yaz.\n" : string.Empty;
        return $"{todo}    [JsonIgnore]\n    public IReadOnlyCollection<string> Permissions => [{string.Join(", ", list)}];";
    }

    private static string CachingMembers(SolutionLayout layout, RequestFile request, ClassDeclarationSyntax cls, List<string> usings)
    {
        usings.Add("System.Text.Json.Serialization");
        string? entity = EntityReader.List(layout).FirstOrDefault(e => e.Name == Names.Singular(request.Feature)).Name;
        string tags;
        if (entity is not null)
        {
            usings.Add(EntityReader.Read(layout, entity).Namespace);
            tags = $"[CanFx.Application.CacheTags.Entity<{entity}>()]";
        }
        else
        {
            tags = "[] // TODO(canfxgen): Hangi kayıt değişince silinsin? ör. [CanFx.Application.CacheTags.Entity<Brand>()]";
        }

        if (request.IsCommand)
        {
            return $"""
                    /// <summary>Bu komuttan sonra silinecek önbellek (kayıt değişince zaten kendiliğinden silinir).</summary>
                    [JsonIgnore]
                    public IReadOnlyCollection<string> CacheTagsToRemove => {tags};
                """;
        }

        // Anahtar: sorgunun parametreleri (aynı parametreler → aynı sonuç).
        var parts = new List<string>();
        bool unknown = false;
        foreach (PropertyDeclarationSyntax property in cls.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (property.AccessorList is null || property.AttributeLists.Any(a => a.ToString().Contains("JsonIgnore", StringComparison.Ordinal)))
                continue;

            string type = property.Type.ToString().TrimEnd('?');
            string name = property.Identifier.Text;
            if (type == "PageRequest")
                parts.Add($"{{{name}.Page}},{{{name}.PageSize}}");
            else if (IsSimple(type))
                parts.Add($"{{{name}}}");
            else
                unknown = true;
        }

        string key = $"$\"{request.Name.Replace("Query", string.Empty, StringComparison.Ordinal)}({string.Join(",", parts)})\"";
        string keyTodo = unknown ? "\n    // TODO(canfxgen): Anahtara karmaşık parametrelerin (liste, nesne) ayırt edici değerlerini ekle." : string.Empty;
        return $"""
                /// <summary>Önbellek anahtarı: aynı parametreler aynı sonucu verir.</summary>{keyTodo}
                [JsonIgnore]
                public string CacheKey => {key};

                [JsonIgnore]
                public IReadOnlyCollection<string> CacheTags => {tags};
            """;
    }

    private static bool IsSimple(string type) =>
        type is "string" or "String" or "int" or "long" or "short" or "byte" or "bool" or "decimal" or "double" or "float"
            or "Guid" or "DateTime" or "DateTimeOffset" or "DateOnly" or "TimeOnly" or "TimeSpan"
            or "Int32" or "Int64" or "Boolean" or "Decimal";

    private static TextEdit AddMembers(string text, ClassDeclarationSyntax cls, string body)
    {
        // Kapanış süslüsünün satır başına; üye varsa araya bir boş satır.
        int close = cls.CloseBraceToken.SpanStart;
        int lineStart = text.LastIndexOf('\n', close - 1) + 1;
        string prefix = cls.Members.Count > 0 ? "\n" : string.Empty;
        return new TextEdit(lineStart, 0, prefix + body.TrimEnd() + "\n");
    }

    private static TextEdit RemoveMember(string text, MemberDeclarationSyntax member)
    {
        // Üyenin satırları (önündeki yorumlar ve attribute'lar dahil) silinir.
        int start = text.LastIndexOf('\n', member.FullSpan.Start > 0 ? member.FullSpan.Start - 1 : 0) + 1;
        int end = text.IndexOf('\n', member.Span.End);
        end = end < 0 ? text.Length : end + 1;
        start = Math.Min(start, member.FullSpan.Start);
        return new TextEdit(start, end - start, string.Empty);
    }

    private static string Apply(string text, List<TextEdit> edits)
    {
        var builder = new StringBuilder(text);
        foreach (TextEdit edit in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
        {
            builder.Remove(edit.Start, edit.Length);
            builder.Insert(edit.Start, edit.Text);
        }

        return builder.ToString();
    }

    /// <summary>Gerekiyorsa <c>using</c> ekler (alfabetik yere değil, using bloğunun sonuna).</summary>
    internal static string AddUsing(string text, string ns)
    {
        if (Regex.IsMatch(text, $@"(?m)^\s*using\s+{Regex.Escape(ns)}\s*;"))
            return text;

        MatchCollection usings = UsingLine().Matches(text);
        if (usings.Count == 0)
            return $"using {ns};\n\n{text}";

        Match last = usings[^1];
        int end = last.Index + last.Length;
        return text[..end] + $"\nusing {ns};" + text[end..];
    }

    /// <summary>Silmelerden kalan fazla boş satırları toplar.</summary>
    private static string Tidy(string text)
    {
        text = ExtraBlankLines().Replace(text, "\n\n");
        return BlankBeforeBrace().Replace(text, "\n$1}");
    }

    private static void TransactionWarning(RequestFile request, bool enable, List<string> warnings)
    {
        string handler = Path.Combine(Path.GetDirectoryName(request.Path)!, request.Name + "Handler.cs");
        string source = File.Exists(handler) ? File.ReadAllText(handler) : File.ReadAllText(request.Path);
        bool saves = source.Contains("SaveChangesAsync", StringComparison.Ordinal);
        if (!enable && request.IsCommand && !saves)
            warnings.Add($"{request.Name}: transaction kapanınca değişiklikleri handler kaydetmeli; handler'a IUnitOfWork ekleyip sonunda 'await _unitOfWork.SaveChangesAsync(cancellationToken);' çağır.");
        if (enable && saves)
            warnings.Add($"{request.Name}: handler zaten kaydediyor (SaveChangesAsync); transaction açıkken bu satır gereksiz, silebilirsin.");
    }

    // ------------------------------------------------------------------ dinamik sorgu

    private static void DynamicQuery(SolutionLayout layout, GenerationPlan plan, string? feature, EntityModel? entity, bool enable, List<string> warnings)
    {
        if (entity is null || feature is null)
        {
            warnings.Add("Dinamik sorgu yalnızca entity ya da özellik için açılıp kapatılır (ör. canfxgen enable dynamic-query Brand).");
            return;
        }

        string folder = Path.Combine(layout.Application, "Features", feature, "Queries", "GetListByDynamic");
        if (enable)
        {
            if (Directory.Exists(folder))
                return;
            plan.Include(CrudGenerator.PlanDynamicQuery(layout, entity, new CrudOptions { Plural = feature }));
            return;
        }

        if (!Directory.Exists(folder))
            return;

        foreach (string file in Directory.EnumerateFiles(folder, "*.cs"))
            plan.Delete(file);

        string E = entity.Name;
        plan.Edit(Path.Combine(layout.Application, "Features", feature, "Profiles", "MappingProfiles.cs"), "dinamik sorgu eşlemesi çıkarıldı",
            text => new InjectResult(Tidy(RemoveLines(text, $"GetListByDynamic{E}ListItemDto", $".Queries.GetListByDynamic;")), null));

        string controller = Path.Combine(layout.WebApi, "Controllers", $"{feature}Controller.cs");
        plan.Edit(controller, "dinamik sorgu uçları çıkarıldı", text =>
        {
            CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();
            List<TextEdit> edits = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text is "GetListByDynamic" or "QueryListByDynamic")
                .Select(m => RemoveMember(text, m))
                .ToList();
            string result = Apply(text, edits);
            return new InjectResult(Tidy(RemoveLines(result, ".Queries.GetListByDynamic;")), null);
        });
    }

    private static string RemoveLines(string text, params string[] containing) =>
        string.Join('\n', text.Split('\n').Where(line => !containing.Any(c => line.Contains(c, StringComparison.Ordinal))));

    [GeneratedRegex(@"(?m)^using\s+[\w.]+\s*;[^\n]*")]
    private static partial Regex UsingLine();

    [GeneratedRegex(@"\n[ \t]*\n([ \t]*\n)+")]
    private static partial Regex ExtraBlankLines();

    [GeneratedRegex(@"\n[ \t]*\n([ \t]*)}")]
    private static partial Regex BlankBeforeBrace();
}
