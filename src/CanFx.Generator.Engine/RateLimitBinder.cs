using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CanFx.Generator.Engine;

/// <summary><c>canfxgen rate-limit</c> seçenekleri: politika appsettings'te yoksa bu değerlerle eklenir.</summary>
public sealed class RateLimitOptions
{
    /// <summary>Bağı kaldır (uç genel sınıra döner).</summary>
    public bool Remove { get; init; }

    public int PermitLimit { get; init; } = 60;

    public string Window { get; init; } = "00:01:00";

    /// <summary>UserOrIp | Ip | Tenant.</summary>
    public string PartitionBy { get; init; } = "UserOrIp";
}

/// <summary>
/// Hız sınırı politikasını uçlara bağlar: controller action'ına (ya da controller'ın tamamına)
/// <c>[EnableRateLimiting("ad")]</c> eklenir; politika appsettings.json'da <c>RateLimiting:Policies</c> altında yoksa
/// varsayılan değerleriyle yazılır. Hazır politikalar: <c>auth</c> (giriş/kayıt, IP başına) ve <c>sensitive</c>.
/// </summary>
public static partial class RateLimitBinder
{
    private const string Attribute = "EnableRateLimiting";

    public static GenerationPlan Plan(SolutionLayout layout, string policy, string target, RateLimitOptions options, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(warnings);

        (string argument, bool builtIn) = Argument(policy.Trim());
        if (options.PartitionBy is not ("UserOrIp" or "Ip" or "Tenant"))
            throw new GeneratorException($"--partition-by geçersiz: '{options.PartitionBy}'. UserOrIp, Ip ya da Tenant olmalı.");
        if (options.PermitLimit < 1)
            throw new GeneratorException("--permit en az 1 olmalı.");
        if (!TimeSpan.TryParse(options.Window, System.Globalization.CultureInfo.InvariantCulture, out TimeSpan window) || window <= TimeSpan.Zero)
            throw new GeneratorException($"--window geçersiz: '{options.Window}' (ör. 00:01:00).");

        var plan = new GenerationPlan();
        List<(string File, string Class, string? Method)> endpoints = Endpoints(layout, target);
        foreach ((string file, string cls, string? method) in endpoints)
        {
            string description = $"{cls}{(method is null ? string.Empty : "." + method)}: {(options.Remove ? "hız sınırı kaldırıldı" : $"[{Attribute}({argument})]")}";
            plan.Edit(file, description, text => Bind(text, cls, method, argument, builtIn, options.Remove));
        }

        if (!options.Remove && !builtIn)
        {
            string settings = Path.Combine(layout.WebApi, "appsettings.json");
            if (File.Exists(settings))
                plan.Edit(settings, $"RateLimiting:Policies:{policy} ({options.PermitLimit} istek / {options.Window}, {options.PartitionBy})", text => AddPolicy(text, policy, options));
            else
                warnings.Add($"appsettings.json bulunamadı; 'RateLimiting:Policies:{policy}' politikasını elle ekle (yoksa uygulama açılışta durur).");
        }

        if (options.Remove && !builtIn)
            warnings.Add($"'{policy}' politikası appsettings'te kaldı; başka uç kullanmıyorsa RateLimiting:Policies'ten silebilirsin.");

        return plan;
    }

    /// <summary>Attribute argümanı: hazır politikalar sabitle, diğerleri metinle.</summary>
    private static (string Argument, bool BuiltIn) Argument(string policy) => policy switch
    {
        "auth" or "can-auth" => ("CanRateLimitPolicies.Auth", true),
        "sensitive" or "can-sensitive" => ("CanRateLimitPolicies.Sensitive", true),
        _ when PolicyName().IsMatch(policy) => ($"\"{policy}\"", false),
        _ => throw new GeneratorException($"Geçersiz politika adı: '{policy}'. Küçük harf, rakam ve tire (ör. reports, export-heavy)."),
    };

    // ------------------------------------------------------------------ uçlar

    /// <summary>
    /// Hedef: istek adı (onu gönderen action'lar), <c>Controller.Action</c>, controller adı ya da özellik / entity
    /// (controller'ın tamamı).
    /// </summary>
    private static List<(string File, string Class, string? Method)> Endpoints(SolutionLayout layout, string target)
    {
        string controllers = Path.Combine(layout.WebApi, "Controllers");
        if (!Directory.Exists(controllers))
            throw new GeneratorException($"Controllers klasörü yok: {controllers}");

        var classes = SolutionLayout.SourceFiles(controllers)
            .SelectMany(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot()
                .DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Select(c => (File: file, Class: c)))
            .ToList();

        // Controller.Action
        if (target.Split('.') is [var controllerName, var actionName])
        {
            var match = classes.FirstOrDefault(c => c.Class.Identifier.Text == controllerName || c.Class.Identifier.Text == controllerName + "Controller");
            if (match.Class?.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == actionName) == true)
                return [(match.File, match.Class.Identifier.Text, actionName)];
            throw new GeneratorException($"'{target}' action'ı bulunamadı.");
        }

        // Controller ya da özellik / entity: controller'ın tamamı
        string[] names = [target, target + "Controller", Names.Plural(target) + "Controller"];
        var controller = classes.FirstOrDefault(c => names.Contains(c.Class.Identifier.Text));
        if (controller.Class is not null && !target.EndsWith("Command", StringComparison.Ordinal) && !target.EndsWith("Query", StringComparison.Ordinal))
            return [(controller.File, controller.Class.Identifier.Text, null)];

        // İstek: onu kullanan action'lar
        var word = new Regex($@"\b{Regex.Escape(target)}\b");
        List<(string, string, string?)> actions = classes
            .SelectMany(c => c.Class.Members.OfType<MethodDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(SyntaxKind.PublicKeyword) && word.IsMatch(m.ToString()))
                .Select(m => (c.File, c.Class.Identifier.Text, (string?)m.Identifier.Text)))
            .ToList();
        return actions.Count > 0
            ? actions
            : throw new GeneratorException($"'{target}' için uç bulunamadı: istek adı (CreateReportCommand), Controller.Action, controller ya da özellik ver.");
    }

    // ------------------------------------------------------------------ attribute

    private static InjectResult Bind(string text, string className, string? methodName, string argument, bool builtIn, bool remove)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();
        ClassDeclarationSyntax? cls = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c => c.Identifier.Text == className);
        MemberDeclarationSyntax? member = methodName is null ? cls
            : cls?.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.Text == methodName);
        if (member is null)
            return new InjectResult(text, $"{className}.{methodName} bulunamadı");

        AttributeSyntax? existing = member.AttributeLists.SelectMany(l => l.Attributes)
            .FirstOrDefault(a => a.Name.ToString() is Attribute or Attribute + "Attribute" or "DisableRateLimiting" or "DisableRateLimitingAttribute");

        string result;
        if (remove)
        {
            if (existing is null)
                return new InjectResult(text, null);

            var list = (AttributeListSyntax)existing.Parent!;
            result = list.Attributes.Count == 1
                ? RemoveLine(text, list)
                : text.Remove(existing.SpanStart, existing.Span.Length).Replace("[, ", "[", StringComparison.Ordinal).Replace(", ]", "]", StringComparison.Ordinal);
            return new InjectResult(result, null);
        }

        string attribute = $"{Attribute}({argument})";
        if (existing is not null)
        {
            if (existing.ToString() == attribute)
                return new InjectResult(text, null);
            result = text[..existing.SpanStart] + attribute + text[existing.Span.End..];
        }
        else
        {
            // Diğer attribute'ların altına (yoksa üyenin üstüne), aynı girintiyle.
            int position = member.AttributeLists.Count > 0 ? member.AttributeLists.Last().Span.End : member.SpanStart;
            int lineStart = text.LastIndexOf('\n', member.SpanStart - 1) + 1;
            string indent = text[lineStart..member.SpanStart];
            result = member.AttributeLists.Count > 0
                ? text[..position] + "\n" + indent + "[" + attribute + "]" + text[position..]
                : text[..position] + "[" + attribute + "]\n" + indent + text[position..];
        }

        result = MechanismEditor.AddUsing(result, "Microsoft.AspNetCore.RateLimiting");
        if (builtIn)
            result = MechanismEditor.AddUsing(result, "CanFx.WebApi.RateLimiting");
        return new InjectResult(result, null);
    }

    private static string RemoveLine(string text, SyntaxNode node)
    {
        int start = text.LastIndexOf('\n', node.SpanStart - 1) + 1;
        int end = text.IndexOf('\n', node.Span.End);
        return text.Remove(start, (end < 0 ? text.Length : end + 1) - start);
    }

    // ------------------------------------------------------------------ appsettings

    /// <summary><c>"Policies": { ... }</c> bloğuna politikayı ekler (yorumlar ve biçim korunur).</summary>
    internal static InjectResult AddPolicy(string text, string policy, RateLimitOptions options)
    {
        Match block = PoliciesBlock().Match(text);
        if (!block.Success)
            return new InjectResult(text, $"appsettings.json'da \"RateLimiting\": {{ \"Policies\": {{ }} }} bulunamadı; '{policy}' politikasını elle ekle.");

        int open = block.Index + block.Length - 1;
        int close = MatchingBrace(text, open);
        if (close < 0)
            return new InjectResult(text, "\"Policies\" bloğu kapanmıyor");

        string inside = text[(open + 1)..close];
        if (Regex.IsMatch(inside, $"\"{Regex.Escape(policy)}\"\\s*:"))
            return new InjectResult(text, null); // zaten tanımlı

        int lineStart = text.LastIndexOf('\n', block.Index) + 1;
        string indent = new(' ', block.Index - lineStart + 2);
        bool hasEntries = Regex.Replace(inside, @"//[^\n]*", string.Empty).Contains('"', StringComparison.Ordinal);
        string entry = $"{indent}\"{policy}\": {{ \"PermitLimit\": {options.PermitLimit}, \"Window\": \"{options.Window}\", \"SegmentsPerWindow\": 1, \"PartitionBy\": \"{options.PartitionBy}\" }}";

        // Bloğun başındaki yorum satırlarından sonra, var olan kayıtların önüne ekle.
        int cursor = text.IndexOf('\n', open);
        while (cursor >= 0 && cursor < close)
        {
            int next = text.IndexOf('\n', cursor + 1);
            if (next < 0 || next > close || !text[(cursor + 1)..next].TrimStart().StartsWith("//", StringComparison.Ordinal))
                break;
            cursor = next;
        }

        int insertAt = cursor >= 0 && cursor < close ? cursor : open + 1;
        string inserted = "\n" + entry + (hasEntries ? "," : string.Empty);
        string result = text[..insertAt] + inserted + text[insertAt..];
        return new InjectResult(result, null);
    }

    private static int MatchingBrace(string text, int open)
    {
        int depth = 0;
        bool inString = false;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (c == '\\')
                    i++;
                else if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
                inString = true;
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                i = text.IndexOf('\n', i) is var e && e >= 0 ? e : text.Length;
            else if (c == '{')
                depth++;
            else if (c == '}' && --depth == 0)
                return i;
        }

        return -1;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex PolicyName();

    [GeneratedRegex("\"Policies\"\\s*:\\s*\\{")]
    private static partial Regex PoliciesBlock();
}
