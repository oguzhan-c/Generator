using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CanFx.Generator.Engine;

/// <summary>Seed diyaloğu için bir alan: tablo sütunu ve örnek veri türü.</summary>
public sealed record SeedField(
    string Name, string Type, string ValueType, bool Nullable, IReadOnlyList<string> EnumValues, string? Related, string SampleKind, IReadOnlyList<string> SampleKinds);

/// <summary>Örnek veri türü ve bir örnek değer (önizleme için).</summary>
public sealed record SampleKindInfo(string Kind, string Example);

/// <summary>
/// Entity'nin seed durumu (IDE diyaloğu bununla açılır): alanlar, var olan zorunlu kayıtlar (JSON ya da HasData),
/// örnek verinin sayısı ve alan başına türü.
/// </summary>
public sealed record SeedDescription(
    string Entity,
    string Plural,
    string Creation,
    bool CanCreate,
    IReadOnlyList<SeedField> Fields,
    IReadOnlyList<SampleKindInfo> Kinds,
    string RequiredStorage,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Records,
    bool SampleExists,
    int SampleCount,
    IReadOnlyDictionary<string, string> SampleKinds,
    int Order,
    string OrderReason);

public static partial class SeederGenerator
{
    /// <summary>Örnek veri türleri: değer tipi → türler (ilki varsayılan değil; varsayılan alan adından tahmin edilir).</summary>
    private static readonly Dictionary<string, string[]> KindsByValueType = new(StringComparer.Ordinal)
    {
        ["string"] = ["NumberedName", "ProductName", "FullName", "FirstName", "LastName", "Email", "Phone", "City", "Sentence", "ShortText", "Code", "Url"],
        ["integer"] = ["Number", "Price"],
        ["decimal"] = ["Price", "Number"],
        ["bool"] = ["Bool"],
        ["date"] = ["PastDate"],
        ["guid"] = ["NewGuid"],
        ["enum"] = ["EnumValue"],
        ["other"] = ["Default"],
    };

    private static readonly Dictionary<string, string> KindExamples = new(StringComparer.Ordinal)
    {
        ["NumberedName"] = "Akıllı Saat 1, Şık Kupa 2 ...",
        ["ProductName"] = "Akıllı Saat",
        ["FullName"] = "Ayşe Yılmaz",
        ["FirstName"] = "Ayşe",
        ["LastName"] = "Yılmaz",
        ["Email"] = "ayse.yilmaz@example.com",
        ["Phone"] = "+905321234567",
        ["City"] = "Aydın",
        ["Sentence"] = "hızlı güvenli yeni modern esnek pratik güçlü sade",
        ["ShortText"] = "modern esnek sade",
        ["Code"] = "CODE-001, CODE-002 ...",
        ["Url"] = "https://example.com/1",
        ["Number"] = "42 (1–100)",
        ["Price"] = "249,90 (10–1000)",
        ["Bool"] = "true / false",
        ["PastDate"] = "son bir yıldan bir tarih",
        ["NewGuid"] = "yeni Guid",
        ["EnumValue"] = "değerlerden biri",
        ["Related"] = "var olan kayıtlardan biri",
        ["Default"] = "varsayılan (elle düzenle)",
    };

    private static readonly JsonSerializerOptions CompactJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Seed diyaloğunun açılış durumu.</summary>
    public static SeedDescription Describe(SolutionLayout layout, EntityModel entity, string? plural = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(entity);

        var n = new CrudNames(layout, entity, plural);
        string seeding = Path.Combine(layout.Persistence, "Seeding");
        (int order, string reason, List<(EntityProperty Property, string Related)> relations) = SuggestOrder(layout, entity, seeding);
        Creation creation = CreationOf(n);

        List<SeedField> fields = creation.Fields.Select(f =>
        {
            string? related = relations.FirstOrDefault(r => r.Property.Name == f.Name).Related;
            string valueType = ValueType(n, f);
            List<string> kinds = [.. KindsByValueType[valueType]];
            if (related is not null)
                kinds.Insert(0, "Related");
            IReadOnlyList<string> values = valueType == "enum" ? EnumValues(layout, f.BareType) : [];
            return new SeedField(f.Name, f.Type, valueType, f.IsNullable, values, related, GuessKind(n, f, related is not null), kinds);
        }).ToList();

        string dataFile = Path.Combine(seeding, "Data", Names.Kebab(n.Plural) + ".json");
        string seedData = Path.Combine(layout.Persistence, "EntityConfigurations", $"{n.Entity}SeedData.cs");
        string storage = "none";
        IReadOnlyList<IReadOnlyDictionary<string, string?>> records = [];
        if (File.Exists(seedData))
        {
            storage = "migration";
            records = ReadHasData(seedData);
        }
        else if (File.Exists(dataFile))
        {
            storage = "json";
            records = ReadDataFile(dataFile, creation);
        }
        else if (File.Exists(Path.Combine(seeding, $"{n.Entity}Seeder.cs")))
        {
            storage = "json";
        }

        string sampleFile = Path.Combine(seeding, $"{n.Entity}SampleSeeder.cs");
        bool sampleExists = File.Exists(sampleFile);
        Dictionary<string, string> sampleKinds = fields.ToDictionary(f => f.Name, f => f.SampleKind, StringComparer.Ordinal);
        int count = 20;
        if (sampleExists)
        {
            string text = File.ReadAllText(sampleFile);
            if (SampleCountLine().Match(text) is { Success: true } m)
                count = int.Parse(m.Groups["count"].Value, CultureInfo.InvariantCulture);
            if (SampleKindLine().Match(text) is { Success: true } k)
            {
                foreach ((string field, string kind) in ParseKinds(k.Groups["kinds"].Value))
                {
                    if (sampleKinds.ContainsKey(field))
                        sampleKinds[field] = kind;
                }
            }
        }

        IReadOnlyList<SampleKindInfo> kindInfos = KindExamples.Select(k => new SampleKindInfo(k.Key, k.Value)).ToList();
        return new SeedDescription(n.Entity, n.Plural, creation.Note, creation.Expression is not null, fields, kindInfos, storage, records,
            sampleExists, count, sampleKinds, order, reason);
    }

    /// <summary><c>Name=ProductName; Price=Price</c> → sözlük.</summary>
    public static Dictionary<string, string> ParseKinds(string text) =>
        text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pair => pair.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(pair => pair.Length == 2 && pair[0].Length > 0 && pair[1].Length > 0)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

    // ------------------------------------------------------------------ türler

    private static bool IsEnum(CrudNames n, MethodParameter field) =>
        n.Model.Properties.FirstOrDefault(p => p.Name == field.Name) is { Kind: PropertyKind.Enum };

    private static string ValueType(CrudNames n, MethodParameter field) => IsEnum(n, field) ? "enum" : field.BareType switch
    {
        "string" or "String" => "string",
        "int" or "Int32" or "long" or "Int64" or "short" or "Int16" or "byte" or "Byte" => "integer",
        "decimal" or "Decimal" or "double" or "Double" or "float" or "Single" => "decimal",
        "bool" or "Boolean" => "bool",
        "DateTimeOffset" or "DateTime" or "DateOnly" or "TimeOnly" => "date",
        "Guid" => "guid",
        _ => "other",
    };

    /// <summary>Alan adına ve tipine göre örnek veri türü.</summary>
    private static string GuessKind(CrudNames n, MethodParameter field, bool related)
    {
        if (related)
            return "Related";
        string name = field.Name.ToLowerInvariant();
        return ValueType(n, field) switch
        {
            "enum" => "EnumValue",
            "string" => name switch
            {
                _ when name.Contains("email", StringComparison.Ordinal) => "Email",
                _ when name.Contains("phone", StringComparison.Ordinal) => "Phone",
                "firstname" => "FirstName",
                "lastname" or "surname" => "LastName",
                "fullname" => "FullName",
                _ when name.Contains("city", StringComparison.Ordinal) => "City",
                _ when name.Contains("description", StringComparison.Ordinal) || name.Contains("note", StringComparison.Ordinal) || name.Contains("comment", StringComparison.Ordinal) => "Sentence",
                _ when name.Contains("code", StringComparison.Ordinal) || name.Contains("sku", StringComparison.Ordinal) || name == "key" || name == "slug" => "Code",
                _ when name.Contains("url", StringComparison.Ordinal) => "Url",
                _ when name.EndsWith("name", StringComparison.Ordinal) || name.Contains("title", StringComparison.Ordinal) => "NumberedName",
                _ => "ShortText",
            },
            "integer" => "Number",
            "decimal" => "Price",
            "bool" => "Bool",
            "date" => "PastDate",
            "guid" => "NewGuid",
            _ => "Default",
        };
    }

    private static Dictionary<string, string> SampleKindsFor(
        CrudNames n, Creation creation, List<(EntityProperty Property, string Related)> relations, IReadOnlyDictionary<string, string>? chosen)
    {
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (MethodParameter field in creation.Fields)
        {
            bool related = relations.Any(r => r.Property.Name == field.Name);
            string guess = GuessKind(n, field, related);
            if (chosen?.GetValueOrDefault(field.Name) is { } kind)
            {
                bool allowed = KindsByValueType[ValueType(n, field)].Contains(kind) || (related && kind == "Related");
                if (!allowed)
                    throw new GeneratorException($"{n.Entity}.{field.Name} ({field.Type}) için '{kind}' örnek türü kullanılamaz; olabilecekler: " +
                        string.Join(", ", (related ? ["Related"] : Array.Empty<string>()).Concat(KindsByValueType[ValueType(n, field)])));
                kinds[field.Name] = kind;
            }
            else
            {
                kinds[field.Name] = guess;
            }
        }

        return kinds;
    }

    private static string KindMarker(IReadOnlyDictionary<string, string> kinds) =>
        kinds.Count == 0 ? "-" : string.Join("; ", kinds.Select(k => $"{k.Key}={k.Value}"));

    /// <summary>Örnek veri türünün C# ifadesi (döngüde <c>sample</c> ve <c>i</c> var).</summary>
    private static string KindExpression(CrudNames n, MethodParameter field, string kind)
    {
        string type = field.BareType;
        string Cast(string expression, string natural) => type switch
        {
            _ when type == natural => expression,
            "int" or "Int32" => $"(int){expression}",
            "long" or "Int64" => $"(long){expression}",
            "short" or "Int16" => $"(short){expression}",
            "byte" or "Byte" => $"(byte){expression}",
            "decimal" or "Decimal" => $"(decimal){expression}",
            "double" or "Double" => $"(double){expression}",
            "float" or "Single" => $"(float){expression}",
            _ => expression,
        };

        return kind switch
        {
            "FirstName" or "LastName" or "FullName" or "Email" or "Phone" or "City" or "ProductName" => $"sample.{kind}()",
            "NumberedName" => "$\"{sample.ProductName()} {i + 1}\"",
            "Sentence" => "sample.Sentence()",
            "ShortText" => "sample.Sentence(3)",
            "Code" => $"$\"{field.Name.ToUpperInvariant()}-{{i + 1:000}}\"",
            "Url" => "$\"https://example.com/{i + 1}\"",
            "Number" => Cast("sample.Number(1, 100)", "int"),
            "Price" => Cast("sample.Price()", "decimal"),
            "Bool" => "sample.Bool()",
            "PastDate" => type switch
            {
                "DateTime" => "sample.PastDate().UtcDateTime",
                "DateOnly" => "DateOnly.FromDateTime(sample.PastDate().UtcDateTime)",
                "TimeOnly" => "TimeOnly.FromDateTime(sample.PastDate().UtcDateTime)",
                _ => "sample.PastDate()",
            },
            "NewGuid" => "Guid.CreateVersion7()",
            "EnumValue" when IsEnum(n, field) => $"sample.Pick(Enum.GetValues<{type}>())",
            _ => $"default! /* TODO(canfxgen): {field.Name} ({field.Type}) için örnek değer */",
        };
    }

    // ------------------------------------------------------------------ kayıtlar

    private static void Write(GenerationPlan plan, string path, string content, string description, bool overwrite)
    {
        if (overwrite && File.Exists(path))
            plan.Edit(path, description, _ => new InjectResult(content.ReplaceLineEndings("\n"), null));
        else
            plan.AddFile(path, content);
    }

    /// <summary>Tablodaki bir kayıt → JSON nesnesi (alan adları camelCase, değerler tipine göre).</summary>
    private static string JsonRecord(CrudNames n, Creation creation, IReadOnlyDictionary<string, string?> record, int index)
    {
        var item = new JsonObject();
        foreach (MethodParameter field in creation.Fields)
        {
            string? raw = Value(record, field.Name);
            string valueType = ValueType(n, field);
            JsonNode? node;
            if (string.IsNullOrEmpty(raw))
            {
                node = field.IsNullable ? null
                    : valueType == "string" ? JsonValue.Create(string.Empty)
                    : throw Invalid(n, index, field, raw, "boş olamaz");
            }
            else
            {
                node = valueType switch
                {
                    "integer" => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? JsonValue.Create(l) : throw Invalid(n, index, field, raw, "tam sayı değil"),
                    "decimal" => decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) ? JsonValue.Create(d) : throw Invalid(n, index, field, raw, "sayı değil"),
                    "bool" => bool.TryParse(raw, out bool b) ? JsonValue.Create(b) : throw Invalid(n, index, field, raw, "true / false değil"),
                    _ => JsonValue.Create(raw),
                };
            }

            item[Names.Camel(field.Name)] = node;
        }

        return "{ " + string.Join(", ", item.Select(p => $"{JsonSerializer.Serialize(p.Key, CompactJson)}: {p.Value?.ToJsonString(CompactJson) ?? "null"}")) + " }";
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> ReadDataFile(string path, Creation creation)
    {
        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (root is not JsonArray array)
                return [];
            return array.OfType<JsonObject>().Select(o => (IReadOnlyDictionary<string, string?>)o.ToDictionary(
                p => creation.Fields.FirstOrDefault(f => string.Equals(f.Name, p.Key, StringComparison.OrdinalIgnoreCase))?.Name ?? p.Key,
                p => p.Value is null ? null : p.Value.GetValueKind() == JsonValueKind.String ? p.Value.GetValue<string>() : p.Value.ToJsonString(),
                StringComparer.Ordinal)).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>HasData kayıtları: Id'ler korunur, yenilere sıradaki Id (sayı) ya da yeni Guid verilir.</summary>
    private static IEnumerable<string> HasDataRecords(CrudNames n, Creation creation, IReadOnlyList<IReadOnlyDictionary<string, string?>> records)
    {
        bool numeric = n.IdType is "int" or "Int32" or "long" or "Int64";
        long next = numeric
            ? records.Select(r => long.TryParse(Value(r, "Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : 0).DefaultIfEmpty(0).Max() + 1
            : 0;

        int index = 0;
        foreach (IReadOnlyDictionary<string, string?> record in records)
        {
            index++;
            string? id = Value(record, "Id");
            string idLiteral = numeric
                ? (string.IsNullOrEmpty(id) ? next++.ToString(CultureInfo.InvariantCulture) : id) + (n.IdType is "long" or "Int64" ? "L" : string.Empty)
                : $"Guid.Parse(\"{(string.IsNullOrEmpty(id) ? Guid.CreateVersion7().ToString() : id)}\")";
            IEnumerable<string> values = creation.Fields.Select(f => $"{f.Name} = {CSharpLiteral(n, f, Value(record, f.Name), index)}");
            yield return $"new {{ Id = {idLiteral}, {string.Join(", ", values)} }}";
        }
    }

    private static string CSharpLiteral(CrudNames n, MethodParameter field, string? raw, int index)
    {
        string valueType = ValueType(n, field);
        if (string.IsNullOrEmpty(raw))
        {
            return field.IsNullable ? $"({field.Type})null"
                : valueType == "string" ? "\"\""
                : throw Invalid(n, index, field, raw, "boş olamaz");
        }

        string type = field.BareType;
        return valueType switch
        {
            "string" => SymbolDisplay.FormatLiteral(raw, quote: true),
            "integer" => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                ? type switch { "long" or "Int64" => raw + "L", "short" or "Int16" => $"(short){raw}", "byte" or "Byte" => $"(byte){raw}", _ => raw }
                : throw Invalid(n, index, field, raw, "tam sayı değil"),
            "decimal" => decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d)
                ? d.ToString(CultureInfo.InvariantCulture) + (type switch { "double" or "Double" => "d", "float" or "Single" => "f", _ => "m" })
                : throw Invalid(n, index, field, raw, "sayı değil"),
            "bool" => bool.TryParse(raw, out bool b) ? (b ? "true" : "false") : throw Invalid(n, index, field, raw, "true / false değil"),
            "enum" => $"{type}.{raw}",
            "guid" => $"Guid.Parse(\"{raw}\")",
            "date" => $"{type}.Parse(\"{raw}\", System.Globalization.CultureInfo.InvariantCulture)",
            _ => raw,
        };
    }

    /// <summary>Var olan HasData dosyasındaki kayıtlar (<c>new { Id = 1, Name = "Kırmızı" },</c> satırları).</summary>
    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> ReadHasData(string path)
    {
        if (!File.Exists(path))
            return [];
        var records = new List<IReadOnlyDictionary<string, string?>>();
        foreach (Match match in HasDataLine().Matches(File.ReadAllText(path)))
        {
            var record = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (string part in SplitTopLevel(match.Groups["body"].Value))
            {
                int equals = part.IndexOf('=', StringComparison.Ordinal);
                if (equals > 0)
                    record[part[..equals].Trim()] = Unliteral(part[(equals + 1)..].Trim());
            }

            records.Add(record);
        }

        return records;
    }

    private static IEnumerable<string> SplitTopLevel(string text)
    {
        int depth = 0, start = 0;
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' && (i == 0 || text[i - 1] != '\\'))
                quoted = !quoted;
            else if (!quoted && c is '(' or '[' or '{')
                depth++;
            else if (!quoted && c is ')' or ']' or '}')
                depth--;
            else if (!quoted && depth == 0 && c == ',')
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        if (start < text.Length)
            yield return text[start..];
    }

    private static string? Unliteral(string literal)
    {
        if (literal == "null" || literal.EndsWith(")null", StringComparison.Ordinal))
            return null;
        if (QuotedArgument().Match(literal) is { Success: true } call)       // Guid.Parse("..."), DateTimeOffset.Parse("...", ...)
            return Regex.Unescape(call.Groups["value"].Value);
        if (literal.StartsWith('"') && literal.EndsWith('"') && literal.Length >= 2)
            return Regex.Unescape(literal[1..^1]);
        if (literal.StartsWith('(') && literal.IndexOf(')', StringComparison.Ordinal) is var close and > 0)  // (short)5
            literal = literal[(close + 1)..];
        if (literal.Length > 1 && (char.IsDigit(literal[0]) || literal[0] == '-') && literal[^1] is 'm' or 'M' or 'd' or 'D' or 'f' or 'F' or 'L' or 'l')
            return literal[..^1];
        if (literal.Contains('.', StringComparison.Ordinal) && char.IsLetter(literal[0]))   // Status.Active
            return literal[(literal.LastIndexOf('.') + 1)..];
        return literal;
    }

    private static string? Value(IReadOnlyDictionary<string, string?> record, string field) =>
        record.TryGetValue(field, out string? value) ? value
        : record.FirstOrDefault(p => string.Equals(p.Key, field, StringComparison.OrdinalIgnoreCase)).Value;

    private static GeneratorException Invalid(CrudNames n, int index, MethodParameter field, string? raw, string problem) =>
        new($"{n.Entity}: {index}. kayıt, {field.Name} = '{raw}' {problem}.");

    /// <summary>Domain'deki enum'un değerleri.</summary>
    private static IReadOnlyList<string> EnumValues(SolutionLayout layout, string name)
    {
        foreach (string file in SolutionLayout.SourceFiles(layout.Domain))
        {
            string text = File.ReadAllText(file);
            if (!text.Contains("enum " + name, StringComparison.Ordinal))
                continue;
            EnumDeclarationSyntax? declaration = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot()
                .DescendantNodes().OfType<EnumDeclarationSyntax>().FirstOrDefault(e => e.Identifier.Text == name);
            if (declaration is not null)
                return declaration.Members.Select(m => m.Identifier.Text).ToList();
        }

        return [];
    }

    [GeneratedRegex(@"private const int Count = (?<count>\d+);")]
    private static partial Regex SampleCountLine();

    [GeneratedRegex(@"Değer türleri \(Rider: Add Seed Data → Sample data\): (?<kinds>[^\r\n]*)")]
    private static partial Regex SampleKindLine();

    [GeneratedRegex(@"(?m)^\s*new \{ (?<body>.*) \},?\s*$")]
    private static partial Regex HasDataLine();

    [GeneratedRegex(@"^[\w.]+\.Parse\(""(?<value>(?:[^""\\]|\\.)*)""")]
    private static partial Regex QuotedArgument();
}
