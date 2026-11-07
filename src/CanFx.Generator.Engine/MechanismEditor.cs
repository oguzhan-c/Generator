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

/// <summary>Bir mekanizmanın hedefteki durumu: <paramref name="Enabled"/> / <paramref name="Total"/> istekte açık.</summary>
public sealed record MechanismStatus(Mechanism Mechanism, int Enabled, int Total);

/// <summary>Hedefin (istek ya da özellik) mekanizma durumu.</summary>
public sealed record MechanismReport(string Target, bool SingleRequest, int Requests, IReadOnlyList<MechanismStatus> Mechanisms);

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

                // Transaction ve log yalnızca komutlarda anlamlı (sorgu veri değiştirmez); kapatırken her yerden kaldırılır.
                if (enable && CommandsOnly(mechanism) && !request.IsCommand)
                    continue;

                if (mechanism == Mechanism.Transaction && request.IsCommand)
                    HandlerSave(plan, request, enable, warnings);

                RequestFile current = request;
                plan.Edit(request.Path, $"{request.Name}: {Name(mechanism)} {(enable ? "açıldı" : "kapatıldı")}",
                    text => Edit(layout, current, text, mechanism, enable));
            }
        }

        return plan;
    }

    /// <summary>
    /// Hedefteki mekanizmaların durumu: her biri kaç istekte açık / kaç istek için geçerli (önbellek dinamik sorguda
    /// geçerli değil). Dinamik sorgu yalnızca entity / özellik hedefinde (GetListByDynamic var mı).
    /// </summary>
    public static MechanismReport Status(SolutionLayout layout, string target)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        (string? feature, EntityModel? entity, List<RequestFile> requests) = Resolve(layout, target);
        var states = new List<MechanismStatus>();
        foreach (Mechanism mechanism in new[] { Mechanism.Caching, Mechanism.Logging, Mechanism.Transaction, Mechanism.Secured })
        {
            string[] names = Interfaces(mechanism);
            List<RequestFile> applicable = mechanism == Mechanism.Caching ? requests.Where(r => !r.HasDynamicQuery).ToList()
                : CommandsOnly(mechanism) ? requests.Where(r => r.IsCommand).ToList()
                : requests;
            states.Add(new MechanismStatus(mechanism, applicable.Count(r => r.Interfaces.Any(names.Contains)), applicable.Count));
        }

        bool single = target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || requests.Any(r => r.Name == target);
        if (!single && entity is not null && feature is not null)
        {
            bool exists = Directory.Exists(Path.Combine(layout.Application, "Features", feature, "Queries", "GetListByDynamic"));
            states.Add(new MechanismStatus(Mechanism.DynamicQuery, exists ? 1 : 0, 1));
        }

        return new MechanismReport(single ? requests[0].Name : feature ?? target, single, requests.Count, states);
    }

    /// <summary>Yalnızca komutlara uygulanan mekanizmalar (sorgu veri değiştirmez).</summary>
    private static bool CommandsOnly(Mechanism mechanism) => mechanism is Mechanism.Transaction or Mechanism.Logging;

    /// <summary>Komut satırındaki ad (<c>caching</c>, <c>dynamic-query</c> ...).</summary>
    public static string CliName(Mechanism mechanism) => mechanism switch
    {
        Mechanism.Caching => "caching",
        Mechanism.Logging => "logging",
        Mechanism.Transaction => "transaction",
        Mechanism.Secured => "secured",
        _ => "dynamic-query",
    };

    private static string[] Interfaces(Mechanism mechanism) => mechanism switch
    {
        Mechanism.Caching => ["ICachableRequest", "ICacheRemoverRequest"],
        Mechanism.Logging => ["ILoggableRequest"],
        Mechanism.Transaction => ["ITransactionalRequest"],
        _ => ["ISecuredRequest"],
    };

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

    private sealed record RequestFile(string Path, string Name, string Feature, bool IsCommand, bool HasDynamicQuery, IReadOnlyList<string> Interfaces);

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
            yield return new RequestFile(Path.GetFullPath(path), cls.Identifier.Text, feature ?? string.Empty,
                !cls.Identifier.Text.EndsWith("Query", StringComparison.Ordinal), dynamic, cls.BaseList.Types.Select(BaseName).ToList());
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

    /// <summary>
    /// Transaction kapanınca kaydı handler yapar: kaydetmiyorsa <c>IUnitOfWork</c> enjekte edilip her <c>return</c>'den önce
    /// <c>SaveChangesAsync</c> eklenir; yerleştirilemezse handler'a TODO yazılır. Açılınca handler'ın kendi kaydı gereksizdir
    /// (zararsız; yalnızca bildirilir).
    /// </summary>
    private static void HandlerSave(GenerationPlan plan, RequestFile request, bool enable, List<string> warnings)
    {
        string handler = Path.Combine(Path.GetDirectoryName(request.Path)!, request.Name + "Handler.cs");
        if (!File.Exists(handler))
            handler = request.Path; // eski düzen: handler isteğin dosyasında
        string source = File.ReadAllText(handler);
        bool saves = source.Contains("SaveChangesAsync", StringComparison.Ordinal);

        if (enable)
        {
            // Kapatırken (ya da üretirken) eklenen kayıt satırı geri alınır; kullanıcının kendi kaydı yalnızca bildirilir.
            if (source.Contains(SaveMarker, StringComparison.Ordinal))
                plan.Edit(handler, $"{request.Name}Handler: kayıt transaction'a bırakıldı", text => new InjectResult(Tidy(RemoveLines(text, SaveMarker, TransactionTodo)), null));
            else if (saves)
                warnings.Add($"{request.Name}: handler zaten kaydediyor (SaveChangesAsync); transaction açıkken bu satır gereksiz ama zararsız.");
            return;
        }

        if (!saves)
            plan.Edit(handler, $"{request.Name}Handler: değişiklikler handler'da kaydedilir", text => AddSave(text, request.Name + "Handler"));
    }

    private const string SaveMarker = "// transaction kapalı: handler kendisi kaydeder";
    private const string TransactionTodo = "// TODO(canfxgen): transaction kapalı: handler değişiklikleri kendisi kaydetmeli (IUnitOfWork.SaveChangesAsync).";

    private static InjectResult AddSave(string text, string handlerName)
    {
        const string Todo = TransactionTodo;
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();
        ClassDeclarationSyntax? cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == handlerName)
            ?? root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c => c.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == "Handle"));
        MethodDeclarationSyntax? handle = cls?.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.Text == "Handle");
        if (cls is null || handle is null)
            return new InjectResult(text, "handler'da Handle metodu bulunamadı; kaydı elle ekle");

        string className = cls.Identifier.Text;
        string Fallback() => AddTodoAbove(text, className, "Handle", Todo);

        // Yerleştirilemeyen durumlar: ifade gövdeli ya da async olmayan Handle, birincil constructor'sız/çok constructor'lı sınıf.
        if (handle.Body is null || !handle.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return new InjectResult(Fallback(), null);

        var edits = new List<TextEdit>();
        var usings = new List<string>();
        string? unitOfWork = cls.Members.OfType<FieldDeclarationSyntax>()
            .FirstOrDefault(f => f.Declaration.Type.ToString() == "IUnitOfWork")?.Declaration.Variables[0].Identifier.Text;

        if (unitOfWork is null && cls.ParameterList is { } primary)
        {
            unitOfWork = primary.Parameters.FirstOrDefault(p => p.Type?.ToString() == "IUnitOfWork")?.Identifier.Text;
            if (unitOfWork is null)
            {
                unitOfWork = "unitOfWork";
                edits.Add(new TextEdit(primary.CloseParenToken.SpanStart, 0, (primary.Parameters.Count > 0 ? ", " : string.Empty) + "IUnitOfWork unitOfWork"));
                usings.Add("CanFx.Persistence.Repositories");
            }
        }
        else if (unitOfWork is null)
        {
            List<ConstructorDeclarationSyntax> constructors = cls.Members.OfType<ConstructorDeclarationSyntax>().Where(c => !c.Modifiers.Any(SyntaxKind.StaticKeyword)).ToList();
            if (constructors.Count != 1 || constructors[0].Body is not { } body)
                return new InjectResult(Fallback(), null);

            ConstructorDeclarationSyntax ctor = constructors[0];
            unitOfWork = "_unitOfWork";
            SeparatedSyntaxList<ParameterSyntax> parameters = ctor.ParameterList.Parameters;
            string separator = parameters.Count == 0 ? string.Empty
                : text[parameters.Last().Span.End..ctor.ParameterList.CloseParenToken.SpanStart].Contains('\n') || ctor.ParameterList.ToString().Contains('\n')
                    ? ",\n" + Indentation(text, parameters.Last().SpanStart)
                    : ", ";
            edits.Add(new TextEdit(parameters.Count == 0 ? ctor.ParameterList.CloseParenToken.SpanStart : parameters.Last().Span.End, 0, separator + "IUnitOfWork unitOfWork"));
            string bodyIndent = body.Statements.Count > 0 ? Indentation(text, body.Statements.Last().SpanStart) : Indentation(text, body.OpenBraceToken.SpanStart) + "    ";
            int afterLast = body.Statements.Count > 0 ? body.Statements.Last().Span.End : body.OpenBraceToken.Span.End;
            edits.Add(new TextEdit(afterLast, 0, "\n" + bodyIndent + "_unitOfWork = unitOfWork;"));

            FieldDeclarationSyntax? lastField = cls.Members.OfType<FieldDeclarationSyntax>().LastOrDefault();
            if (lastField is not null)
                edits.Add(new TextEdit(lastField.Span.End, 0, "\n" + Indentation(text, lastField.SpanStart) + "private readonly IUnitOfWork _unitOfWork;"));
            else
                edits.Add(new TextEdit(cls.OpenBraceToken.Span.End, 0, "\n" + Indentation(text, ctor.SpanStart) + "private readonly IUnitOfWork _unitOfWork;\n"));
            usings.Add("CanFx.Persistence.Repositories");
        }

        string save = $"await {unitOfWork}.SaveChangesAsync(cancellationToken); {SaveMarker}";
        List<ReturnStatementSyntax> returns = handle.Body.DescendantNodes(n => n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<ReturnStatementSyntax>().ToList();
        bool unplaced = returns.Any(r => r.Parent is not BlockSyntax);
        foreach (ReturnStatementSyntax ret in returns.Where(r => r.Parent is BlockSyntax))
            edits.Add(new TextEdit(ret.SpanStart, 0, save + "\n" + Indentation(text, ret.SpanStart)));
        if (returns.Count == 0)
        {
            string indent = handle.Body.Statements.Count > 0 ? Indentation(text, handle.Body.Statements.Last().SpanStart) : Indentation(text, handle.Body.OpenBraceToken.SpanStart) + "    ";
            int afterLast = handle.Body.Statements.Count > 0 ? handle.Body.Statements.Last().Span.End : handle.Body.OpenBraceToken.Span.End;
            edits.Add(new TextEdit(afterLast, 0, "\n\n" + indent + save));
        }

        string result = Apply(text, edits);
        foreach (string ns in usings)
            result = AddUsing(result, ns);
        if (unplaced)
            result = AddTodoAbove(result, handlerName, "Handle", Todo + " Tek satırlık if/else içindeki return'lerden önce de kaydet.");
        return new InjectResult(result, null);
    }

    private static string Indentation(string text, int position)
    {
        int line = text.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
        int end = line;
        while (end < text.Length && text[end] is ' ' or '\t')
            end++;
        return text[line..end];
    }

    /// <summary>Sınıftaki metodun üstüne (yorumlarıyla birlikte) TODO satırı.</summary>
    private static string AddTodoAbove(string text, string className, string method, string todo)
    {
        if (text.Contains(todo, StringComparison.Ordinal))
            return text;
        MethodDeclarationSyntax? target = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == className)?.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.Text == method);
        if (target is null)
            return text;
        // Metodun yorumlarının (yoksa kendisinin) satırına; aradaki boş satır korunur.
        SyntaxTrivia comment = target.GetLeadingTrivia().FirstOrDefault(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia));
        int position = comment.IsKind(SyntaxKind.None) ? target.SpanStart : comment.SpanStart;
        int at = text.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
        return text[..at] + Indentation(text, target.SpanStart) + todo + "\n" + text[at..];
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
