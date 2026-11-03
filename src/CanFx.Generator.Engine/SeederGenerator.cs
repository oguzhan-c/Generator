using System.Text.RegularExpressions;

namespace CanFx.Generator.Engine;

/// <summary><c>canfxgen generate seeder</c> seçenekleri.</summary>
public sealed class SeederOptions
{
    /// <summary>Her ortamda eklenen zorunlu veri: <c>Seeding/Data/{çoğul}.json</c> + <c>{Entity}Seeder</c>.</summary>
    public bool Required { get; init; } = true;

    /// <summary>Yalnızca geliştirmede eklenen örnek veri: <c>{Entity}SampleSeeder</c> (SampleData ile).</summary>
    public bool Sample { get; init; } = true;

    /// <summary>Örnek kayıt sayısı.</summary>
    public int SampleCount { get; init; } = 20;

    /// <summary>
    /// Zorunlu veri JSON + seeder yerine EF Core <c>HasData</c> ile migration'a yazılır (Id'ler sabit). Migration ile
    /// dağıtılan, nadiren değişen veriler için (ör. sabit durum listesi).
    /// </summary>
    public bool HasData { get; init; }

    public string? Plural { get; init; }

    /// <summary>
    /// Zorunlu verinin kayıtları (alan adı → metin değer; IDE'deki tablodan). Verilirse veri dosyası (ya da
    /// <see cref="HasData"/>'da migration verisi) bunlarla yazılır; verilmezse boş bir dosya oluşturulur.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string?>>? Records { get; init; }

    /// <summary>Örnek veride alan başına değer türü (alan → tür, ör. <c>Name=City</c>; türler <see cref="SeederGenerator.Describe"/>'da); yazılmayanlar tahmin edilir.</summary>
    public IReadOnlyDictionary<string, string>? SampleKinds { get; init; }
}

/// <summary>
/// Entity için seed: zorunlu veri (JSON dosyasından, eksik olanlar eklenir), geliştirme örnek verisi
/// (<c>SampleData</c>) ve isteğe bağlı <c>HasData</c>. Sıra, ilişkili entity'lerin seeder'larına göre önerilir.
/// </summary>
public static partial class SeederGenerator
{
    private const string Header = "// canfxgen ile üretildi; artık senin kodun, istediğin gibi değiştir.";

    /// <summary>Doğal anahtar adayları: zorunlu veride kayıt bunlarla eşleşirse tekrar eklenmez.</summary>
    private static readonly string[] KeyCandidates = ["Code", "Key", "Slug", "Name", "Title", "Email"];

    public static GenerationPlan Plan(SolutionLayout layout, EntityModel entity, SeederOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(entity);
        options ??= new SeederOptions();
        if (!options.Required && !options.Sample && !options.HasData)
            throw new GeneratorException("Üretilecek bir şey yok: zorunlu veri, örnek veri ya da HasData'dan en az biri seçilmeli.");

        var n = new CrudNames(layout, entity, options.Plural);
        string seeding = Path.Combine(layout.Persistence, "Seeding");
        (int order, string reason, List<(EntityProperty Property, string Related)> relations) = SuggestOrder(layout, entity, seeding);
        List<string> relatedNamespaces = relations
            .Select(r => EntityReader.Read(layout, r.Related).Namespace)
            .Where(ns => ns != entity.Namespace)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Creation creation = CreationOf(n);
        string fileName = Names.Kebab(n.Plural);

        var plan = new GenerationPlan();
        if (options.Required && !options.HasData)
        {
            string data = Path.Combine(seeding, "Data", fileName + ".json");
            Write(plan, data, DataFile(n, creation, options.Records), "zorunlu veri kayıtları", overwrite: options.Records is not null);
            plan.AddFile(Path.Combine(seeding, $"{n.Entity}Seeder.cs"), RequiredSeeder(n, creation, order, reason, fileName));
            plan.Edit(Path.Combine(layout.Persistence, Path.GetFileName(layout.Persistence) + ".csproj"), "seed dosyaları çıktıya kopyalanır", CopySeedFiles);
        }

        if (options.Sample)
        {
            Dictionary<string, string> kinds = SampleKindsFor(n, creation, relations, options.SampleKinds);
            Write(plan, Path.Combine(seeding, $"{n.Entity}SampleSeeder.cs"),
                SampleSeeder(n, creation, order + 1, options.SampleCount, relations, relatedNamespaces, kinds), "örnek veri", overwrite: options.SampleKinds is not null);
        }

        if (options.HasData)
        {
            string seedData = Path.Combine(layout.Persistence, "EntityConfigurations", $"{n.Entity}SeedData.cs");
            IReadOnlyList<IReadOnlyDictionary<string, string?>>? records = options.Records;
            Write(plan, seedData, HasDataFile(n, creation, records ?? ReadHasData(seedData)), "migration verisi (HasData)", overwrite: records is not null);
            string configuration = Path.Combine(layout.Persistence, "EntityConfigurations", $"{n.Entity}Configuration.cs");
            plan.Edit(configuration, $"builder.HasData({n.Entity}SeedData.Items)", text => AddHasData(text, n.Entity));
        }

        return plan;
    }

    // ------------------------------------------------------------------ sıra

    /// <summary>Önerilen seeder sırası ve gerekçesi.</summary>
    public static (int Order, string Reason) SuggestedOrder(SolutionLayout layout, EntityModel entity)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(entity);
        (int order, string reason, _) = SuggestOrder(layout, entity, Path.Combine(layout.Persistence, "Seeding"));
        return (order, reason);
    }

    /// <summary>
    /// Var olan seeder'ların sırasına bakarak öneri: entity'nin <c>{Başka}Id</c> alanları varsa ve o entity'nin seeder'ı
    /// varsa ondan sonra (en büyük + 10); yoksa 100.
    /// </summary>
    internal static (int Order, string Reason, List<(EntityProperty Property, string Related)> Relations) SuggestOrder(SolutionLayout layout, EntityModel entity, string seeding)
    {
        var existing = new Dictionary<string, int>(StringComparer.Ordinal);
        if (Directory.Exists(seeding))
        {
            foreach (string file in Directory.EnumerateFiles(seeding, "*Seeder.cs"))
            {
                string text = File.ReadAllText(file);
                Match cls = SeederClass().Match(text);
                Match ord = OrderProperty().Match(text);
                if (cls.Success && !cls.Groups["entity"].Value.EndsWith("Sample", StringComparison.Ordinal))
                    existing[cls.Groups["entity"].Value] = ord.Success ? int.Parse(ord.Groups["order"].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            }
        }

        HashSet<string> entities = EntityReader.List(layout).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var relations = entity.ScalarProperties
            .Where(p => p.Name.Length > 2 && p.Name.EndsWith("Id", StringComparison.Ordinal) && entities.Contains(p.Name[..^2]))
            .Select(p => (p, p.Name[..^2]))
            .ToList();

        List<(string Related, int Order)> before = relations
            .Where(r => existing.ContainsKey(r.Item2))
            .Select(r => (r.Item2, existing[r.Item2]))
            .ToList();

        if (before.Count == 0)
        {
            string missing = relations.Count == 0 ? string.Empty
                : $" İlişkili entity'lerin ({string.Join(", ", relations.Select(r => r.Item2))}) seeder'ı yok; onlarınki bundan küçük olmalı.";
            return (100, "Bağımlı olduğu seeder yok." + missing, relations);
        }

        int order = before.Max(b => b.Order) + 10;
        string after = string.Join(", ", before.Select(b => $"{b.Related}Seeder ({b.Order})"));
        string first = string.Join(", ", before.Select(b => b.Related));
        return (order, $"{after} sonrasında: {first} kayıtları önce eklenmeli.", relations);
    }

    // ------------------------------------------------------------------ oluşturma

    private sealed record Creation(IReadOnlyList<MethodParameter> Fields, Func<IReadOnlyList<string>, string>? Expression, string Note);

    /// <summary>Entity nasıl oluşturulur: fabrika metodu, public constructor ya da public setter'lar.</summary>
    private static Creation CreationOf(CrudNames n)
    {
        string E = n.Entity;
        if (n.Model.Factory is { } factory)
        {
            string suffix = factory.ReturnsResult ? ".OrThrow()" : string.Empty;
            return new Creation(n.CreateFields, values => $"{E}.Create({string.Join(", ", values)}){suffix}", $"{E}.Create(...) ile");
        }

        EntityMethod? constructor = n.Model.PublicConstructors.OrderByDescending(c => c.Parameters.Count).FirstOrDefault();
        if (constructor is { Parameters.Count: > 0 })
        {
            List<MethodParameter> fields = constructor.Parameters.Select(p => p with { Name = Names.Pascal(p.Name) }).ToList();
            return new Creation(fields, values => $"new {E}({string.Join(", ", values)})", $"new {E}(...) ile");
        }

        List<MethodParameter> settable = n.Model.ScalarProperties
            .Where(p => p.Name != "Id" && p.HasPublicSetter)
            .Select(p => new MethodParameter(p.Name, p.Type))
            .ToList();
        if (constructor is not null && settable.Count > 0)
        {
            return new Creation(settable, values => $"new {E} {{ {string.Join(", ", settable.Select((f, i) => $"{f.Name} = {values[i]}"))} }}", "public setter'larla");
        }

        List<MethodParameter> all = n.Model.ScalarProperties.Where(p => p.Name != "Id").Select(p => new MethodParameter(p.Name, p.Type)).ToList();
        return new Creation(all, null, "oluşturulamadı");
    }

    private static string Create(CrudNames n, Creation creation, IReadOnlyList<string> values) =>
        creation.Expression?.Invoke(values) ?? $"New{n.Entity}()";

    /// <summary>Entity oluşturulamıyorsa seeder'a eklenen yer tutucu metot.</summary>
    private static string TodoFactory(CrudNames n, Creation creation) => creation.Expression is not null ? string.Empty : $$"""


            // TODO(canfxgen): {{n.Entity}}'de fabrika metodu ('public static {{n.Entity}} Create(...)'), public constructor ya da public
            // setter yok. Birini ekleyip bu dosyayı silip yeniden üret (canfxgen generate seeder {{n.Entity}}) ya da burada oluştur.
            private static {{n.Entity}} New{{n.Entity}}() =>
                throw new NotImplementedException("TODO(canfxgen): {{n.Entity}} oluşturulmadı.");
        """;

    // ------------------------------------------------------------------ zorunlu veri

    private static string DataFile(CrudNames n, Creation creation, IReadOnlyList<IReadOnlyDictionary<string, string?>>? records)
    {
        string shape = string.Join(", ", creation.Fields.Select(f => $"\"{Names.Camel(f.Name)}\": {JsonExample(n, f)}"));
        string items = records is { Count: > 0 }
            ? string.Join(",\n", records.Select((r, i) => "  " + JsonRecord(n, creation, r, i + 1))) + "\n"
            : string.Empty;
        string todo = records is null
            ? "\n// TODO(canfxgen): Kayıtları ekle (Rider: entity'ye sağ tık → CanFx → Add Seed Data) ya da buraya yaz."
            : string.Empty;
        return $$"""
            // {{n.Words}} başlangıç verisi: HER ORTAMDA eklenir ({{n.Entity}}Seeder). Var olan kayıtlara dokunulmaz, yalnızca eksikler eklenir.
            // Bir kayıt: { {{shape}} }{{todo}}
            // Örnek/sahte veri buraya değil, {{n.Entity}}SampleSeeder'a (yalnızca geliştirmede).
            [
            {{items}}]

            """;
    }

    private static string RequiredSeeder(CrudNames n, Creation creation, int order, string reason, string fileName)
    {
        string E = n.Entity;
        MethodParameter? key = KeyCandidates
            .Select(k => creation.Fields.FirstOrDefault(f => f.Name == k && f.IsString))
            .FirstOrDefault(f => f is not null);

        // Enum'lar JSON'da adıyla yazılır ("Active"): kayıtta metin, oluştururken çevrilir.
        string create = Create(n, creation, creation.Fields
            .Select(f => IsEnum(n, f) ? $"Enum.Parse<{f.BareType}>(item.{f.Name})" : $"item.{f.Name}")
            .ToList());
        string record = string.Join(", ", creation.Fields.Select(f => $"{(IsEnum(n, f) ? "string" : f.Type)} {f.Name}"));
        string filter = n.Model.IsSoftDeletable || n.Model.IsMultiTenant ? ".IgnoreQueryFilters()" : string.Empty;

        string body = key is not null
            ? $$"""
                    // Eşleşme {{key.Name}} ile: dosyaya eklenen yeni kayıtlar sonraki açılışta eklenir, var olanlar değişmez.
                    HashSet<string> existing = (await db.Set<{{E}}>(){{filter}}.Select(x => x.{{key.Name}}).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
                    foreach ({{E}}Seed item in items.Where(i => !existing.Contains(i.{{key.Name}})))
                        db.Set<{{E}}>().Add({{create}});
            """
            : $$"""
                    // Doğal anahtar (Code, Name ...) bulunamadı: yalnızca tablo boşsa eklenir.
                    // TODO(canfxgen): Benzersiz bir alan varsa onunla eşleştir (eksik kayıtlar sonradan da eklensin).
                    if (await db.Set<{{E}}>(){{filter}}.AnyAsync(cancellationToken))
                        return;
                    foreach ({{E}}Seed item in items)
                        db.Set<{{E}}>().Add({{create}});
            """;

        return $$"""
            {{Header}}
            using CanFx.Application;
            using CanFx.Persistence.Seeding;
            using Microsoft.EntityFrameworkCore;
            using {{n.Root}}.Persistence.Contexts;
            using {{n.Model.Namespace}};

            namespace {{n.Root}}.Persistence.Seeding;

            /// <summary>
            /// {{n.Words}} başlangıç verisi (her ortamda): <c>Seeding/Data/{{fileName}}.json</c>'daki kayıtlar ({{creation.Note}}).
            /// </summary>
            public sealed class {{E}}Seeder(BaseDbContext db) : IDataSeeder
            {
                /// <summary>Çalışma sırası (küçük önce). {{reason}}</summary>
                public int Order => {{order}};

                public async Task SeedAsync(DataSeedContext context, CancellationToken cancellationToken)
                {
                    // TODO(canfxgen): Tenant'a özel veriyse host'ta çalışmasın: if (context.IsHost) return;
                    IReadOnlyList<{{E}}Seed> items = await SeedFiles.ReadJsonAsync<{{E}}Seed>("Seeding/Data/{{fileName}}.json", cancellationToken);
                    if (items.Count == 0)
                        return;

            {{body}}

                    await db.SaveChangesAsync(cancellationToken);
                }{{TodoFactory(n, creation)}}
            }

            /// <summary><c>{{fileName}}.json</c>'daki bir kayıt (alan adları JSON'da camelCase).</summary>
            public sealed record {{E}}Seed({{record}});

            """;
    }

    // ------------------------------------------------------------------ örnek veri

    private static string SampleSeeder(
        CrudNames n, Creation creation, int order, int count, List<(EntityProperty Property, string Related)> relations, List<string> relatedNamespaces,
        IReadOnlyDictionary<string, string> kinds)
    {
        string E = n.Entity;
        var preload = new List<string>();
        var values = new List<string>();
        bool usesFit = false;

        foreach (MethodParameter field in creation.Fields)
        {
            int index = relations.FindIndex(r => r.Property.Name == field.Name);
            string kind = kinds.GetValueOrDefault(field.Name) ?? GuessKind(n, field, index >= 0);
            if (index >= 0 && kind == "Related")
            {
                (EntityProperty _, string related) = relations[index];
                string list = Names.Camel(related) + "Ids";
                preload.Add($"        List<{field.BareType}> {list} = await db.Set<{related}>().Select(x => x.Id).ToListAsync(cancellationToken);");
                preload.Add($"        if ({list}.Count == 0)\n            return; // önce {related} kayıtları gerekir");
                values.Add($"sample.Pick({list})");
                continue;
            }

            string value = KindExpression(n, field, kind);
            if (field.IsString && n.Model.MaxLengthConstant(field.Name) is { } max)
            {
                value = $"Fit({value}, {E}.{max})";
                usesFit = true;
            }

            values.Add(value);
        }

        string create = Create(n, creation, values);
        string loads = preload.Count == 0 ? string.Empty : "\n" + string.Join("\n", preload) + "\n";
        string fit = usesFit ? "\n\n    private static string Fit(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];" : string.Empty;
        string relatedUsings = string.Join(string.Empty, relatedNamespaces.Select(ns => $"using {ns};\n"));
        string filter = n.Model.IsSoftDeletable || n.Model.IsMultiTenant ? ".IgnoreQueryFilters()" : string.Empty;

        return $$"""
            {{Header}}
            using CanFx.Application;
            using CanFx.Persistence.Seeding;
            using Microsoft.EntityFrameworkCore;
            using {{n.Root}}.Persistence.Contexts;
            {{relatedUsings}}using {{n.Model.Namespace}};

            namespace {{n.Root}}.Persistence.Seeding;

            /// <summary>
            /// {{n.Words}} örnek verisi: YALNIZCA GELİŞTİRMEDE (ekranlar boş görünmesin). Değerler <see cref="SampleData"/>
            /// ile üretilir; aynı tohumla her seferinde aynıdır.
            /// </summary>
            public sealed class {{E}}SampleSeeder(BaseDbContext db) : IDataSeeder
            {
                private const int Count = {{count}};

                /// <summary>Zorunlu veriden ({{E}}Seeder) hemen sonra.</summary>
                public int Order => {{order}};

                public bool DevelopmentOnly => true;

                public async Task SeedAsync(DataSeedContext context, CancellationToken cancellationToken)
                {
                    if (await db.Set<{{E}}>(){{filter}}.CountAsync(cancellationToken) >= Count)
                        return;
            {{loads}}
                    // Değer türleri (Rider: Add Seed Data → Sample data): {{KindMarker(kinds)}}
                    var sample = new SampleData(seed: {{Math.Abs(StableHash(E)) % 1000}});
                    for (int i = 0; i < Count; i++)
                        db.Set<{{E}}>().Add({{create}});

                    await db.SaveChangesAsync(cancellationToken);
                }{{fit}}{{TodoFactory(n, creation)}}
            }

            """;
    }


    private static string JsonExample(CrudNames n, MethodParameter field)
    {
        if (n.Model.Properties.FirstOrDefault(p => p.Name == field.Name) is { Kind: PropertyKind.Enum })
            return $"\"{field.BareType} değeri\"";
        return field.BareType switch
        {
            "string" or "String" => "\"...\"",
            "bool" or "Boolean" => "true",
            "Guid" => "\"00000000-0000-0000-0000-000000000000\"",
            "DateTimeOffset" or "DateTime" => "\"2026-01-01T00:00:00Z\"",
            "DateOnly" => "\"2026-01-01\"",
            _ => "0",
        };
    }

    private static int StableHash(string text)
    {
        int hash = 17;
        foreach (char c in text)
            hash = unchecked((hash * 31) + c);
        return hash == int.MinValue ? 0 : hash;
    }

    // ------------------------------------------------------------------ HasData

    private static string HasDataFile(CrudNames n, Creation creation, IReadOnlyList<IReadOnlyDictionary<string, string?>> records)
    {
        string items = records.Count > 0
            ? string.Join("\n", HasDataRecords(n, creation, records).Select(r => "        " + r + ","))
            : $"        // TODO(canfxgen): Kayıtları ekle (Rider: Add Seed Data → Initial data → Migration), ör.:\n" +
              $"        // new {{ {string.Join(", ", new[] { $"Id = {IdExample(n.IdType)}" }.Concat(n.CreateFields.Take(2).Select(f => $"{f.Name} = {JsonExample(n, f).Replace("\"...\"", "\"Örnek\"", StringComparison.Ordinal)}")))} }},";
        return $$"""
            {{Header}}
            namespace {{n.Root}}.Persistence.EntityConfigurations;

            /// <summary>
            /// {{n.Words}} için EF Core <c>HasData</c>: kayıtlar migration'a yazılır ve migration'la birlikte her veritabanına gider
            /// ({{n.Entity}}Configuration'da <c>builder.HasData(Items)</c>). Id'ler SABİT olmalı (her migration'da aynı); değişiklik
            /// yeni bir migration gerektirir. Sık değişen ya da tenant'a özel veri için {{n.Entity}}Seeder'ı kullan.
            /// Anonim nesne kullanılır: entity'nin setter'ları özel olsa da çalışır.
            /// </summary>
            internal static class {{n.Entity}}SeedData
            {
                public static readonly object[] Items =
                [
            {{items}}
                ];
            }

            """;
    }

    private static string IdExample(string idType) => idType switch
    {
        "Guid" => "Guid.Parse(\"0198a2c0-0000-7000-8000-000000000001\")",
        "long" or "Int64" => "1L",
        _ => "1",
    };

    private static InjectResult AddHasData(string text, string entity)
    {
        string line = $"builder.HasData({entity}SeedData.Items);";
        if (text.Contains(line, StringComparison.Ordinal))
            return new InjectResult(text, null);

        Match table = ToTableLine().Match(text);
        if (!table.Success)
            return new InjectResult(text, $"'builder.ToTable(...)' satırı bulunamadı; Configure'a elle ekle: {line}");

        string indent = table.Groups["indent"].Value;
        int end = table.Index + table.Length;
        return new InjectResult(text[..end] + "\n" + indent + line + " // migration'a yazılır (Seeding: " + entity + "SeedData)" + text[end..], null);
    }

    // ------------------------------------------------------------------ csproj

    private static InjectResult CopySeedFiles(string text)
    {
        if (text.Contains(@"Seeding\Data", StringComparison.Ordinal))
            return new InjectResult(text, null);

        if (text.TrimEnd().EndsWith("/>", StringComparison.Ordinal) && !text.Contains("</Project>", StringComparison.Ordinal))
            text = text.TrimEnd()[..^2].TrimEnd() + ">\n</Project>\n"; // <Project Sdk="..." />

        int close = text.LastIndexOf("</Project>", StringComparison.Ordinal);
        if (close < 0)
            return new InjectResult(text, "</Project> bulunamadı");

        const string item = """
              <ItemGroup>
                <!-- Seed verisi (canfxgen generate seeder): çıktıya kopyalanır, SeedFiles.ReadJsonAsync okur. -->
                <None Update="Seeding\Data\*.json" CopyToOutputDirectory="PreserveNewest" />
              </ItemGroup>


            """;
        return new InjectResult(text[..close] + item + text[close..], null);
    }

    [GeneratedRegex(@"class\s+(?<entity>\w+)Seeder\b")]
    private static partial Regex SeederClass();

    [GeneratedRegex(@"Order\s*=>\s*(?<order>\d+)")]
    private static partial Regex OrderProperty();

    [GeneratedRegex(@"(?m)^(?<indent>[ \t]*)builder\.ToTable\([^\n]*;")]
    private static partial Regex ToTableLine();
}
