using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CanFx.Generator.Engine;
using CanFx.Generator;
using static CanFx.Generator.Prompts;

// canfxgen: CanFx çözümleri için kod üretici (nArchitecture'daki nArchGen'in karşılığı). Ayrıntı: canfxgen --help

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
Arguments arguments;

try
{
    arguments = Arguments.Parse(args);
    if (arguments.Json)
        Prompts.Interactive = false;

    string? command = arguments.Positional(0);
    if (arguments.Flag("version"))
        command = "version";
    if (arguments.Flag("help") && command is not "generate")
        command = "help";

    return command switch
    {
        "new" => New(arguments),
        "generate" or "g" => Generate(arguments),
        "crud" => Crud(arguments, entityIndex: 1),
        "rate-limit" => RateLimit(arguments),
        "add" => Feature(arguments, enable: true),
        "remove" => Feature(arguments, enable: false),
        "enable" => Toggle(arguments, enable: true),
        "disable" => Toggle(arguments, enable: false),
        "mechanisms" => MechanismStatusCommand(arguments),
        "entities" => Entities(arguments),
        "features" => Features(arguments),
        "version" => Print(Version()),
        "help" or null => Print(Help()),
        _ => Fail($"Bilinmeyen komut: {command}\n\n{Help()}", arguments.Json),
    };
}
catch (GeneratorException exception)
{
    return Fail(exception.Message, args.Contains("--json"));
}

// ================================================================ new

int New(Arguments a)
{
    Banner();
    string name = a.Positional(1) ?? Text($"Proje {G("adı")}");
    if (string.IsNullOrWhiteSpace(name))
        throw new GeneratorException("Proje adı gerekli: canfxgen new MyShop");
    if (char.IsLower(name[0]))
    {
        // Namespace'ler C# geleneğine uysun: "test3" → "Test3" (proje ve klasör adı da buna göre).
        name = char.ToUpperInvariant(name[0]) + name[1..];
        Console.WriteLine(Dim($"Proje adı {name} olarak kullanılıyor."));
    }

    var templateArgs = new List<string> { "new", "canfx-monolith", "-n", name, "-o", a.Option("output") ?? name };

    if (a.PassThrough.Count == 0 && Interactive)
    {
        string idKey = Select($"Kimlik {G("anahtar tipi")} (kullanıcı, rol, yetki)?", ["guid", "int", "long"]);
        templateArgs.AddRange(["--IdKeyType", idKey]);

        (string Symbol, string Label, bool Default)[] options =
        [
            ("Otp", "Security: OTP (authenticator)", true),
            ("EmailTwoFactor", "Security: 2FA (e-posta kodu)", true),
            ("Passkey", "Security: Passkey", true),
            ("ExternalLogin", "Security: Dış hesapla giriş (Google/Microsoft/GitHub)", false),
            ("MultiTenancy", "Tenancy: Multi-tenancy", false),
            ("Redis", "Altyapı: Redis", false),
            ("EventBus", "Altyapı: Event bus + outbox", false),
            ("Hangfire", "Altyapı: Hangfire", false),
            ("FileStorage", "Altyapı: Dosya depolama", false),
            ("Search", "Altyapı: Arama", false),
            ("Realtime", "Altyapı: SignalR", false),
            ("Sms", "Altyapı: SMS", false),
            ("AuditTrail", "Modül: Değişiklik geçmişi", true),
            ("Settings", "Modül: Ayarlar", false),
            ("Reporting", "Modül: Raporlama", false),
            ("OpenTelemetry", "Modül: OpenTelemetry", false),
        ];
        IReadOnlyList<string> chosen = MultiSelect($"Hangi {G("özellikler")}?", options.Select(o => (o.Label, o.Default)).ToList());
        foreach ((string symbol, string label, _) in options)
            templateArgs.AddRange([$"--{symbol}", chosen.Contains(label) ? "true" : "false"]);
    }

    templateArgs.AddRange(a.PassThrough);


    Console.WriteLine(Dim("dotnet " + string.Join(' ', templateArgs)));
    using Process process = Process.Start(new ProcessStartInfo("dotnet", templateArgs) { UseShellExecute = false })
        ?? throw new GeneratorException("dotnet çalıştırılamadı.");
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new GeneratorException("Şablon çalışmadı. Şablon kurulu mu? (dotnet new list canfx-monolith)");

    Console.WriteLine($"\n✅ {B(name)} oluşturuldu. Sıradaki adımlar:");
    Console.WriteLine($"   cd {a.Option("output") ?? name}");
    Console.WriteLine("   # entity'yi src/*.Domain/Entities altına yaz, sonra:");
    Console.WriteLine("   canfxgen generate crud <Entity>");
    return 0;
}

// ================================================================ generate

int Generate(Arguments a)
{
    string? what = a.Positional(1);
    if (what is null || a.Flag("help"))
        return Print(GenerateHelp());

    return what switch
    {
        "crud" => Crud(a, entityIndex: 2),
        "entity" => Entity(a),
        "seeder" => Seeder(a),
        "command" => Request(a, isCommand: true),
        "query" => Request(a, isCommand: false),
        "dynamic-query" => DynamicQuery(a),
        _ => Fail($"Bilinmeyen üretim: {what}\n\n{GenerateHelp()}", a.Json),
    };
}

int Crud(Arguments a, int entityIndex)
{
    Banner();
    (SolutionLayout layout, EntityModel entity) = SelectEntity(a, entityIndex);

    (bool caching, bool logging, bool transaction, bool secured, bool dynamic) = Mechanisms(a, includeTransaction: true, includeDynamic: true);
    var options = new CrudOptions
    {
        Plural = a.Option("plural"),
        Caching = caching,
        Logging = logging,
        Transaction = transaction,
        Secured = secured,
        DynamicQuery = dynamic,
    };

    IReadOnlyList<ChangeResult> results = CrudGenerator.Plan(layout, entity, options).Apply();
    bool createTodo = entity.Factory is null && !entity.HasPublicConstructor;
    bool updateTodo = entity.Update is null && !entity.ScalarProperties.Where(p => p.Name != "Id").All(p => p.HasPublicSetter);

    var warnings = new List<string>();
    if (createTodo)
        warnings.Add($"{entity.Name}'de 'Create(...)' fabrika metodu ya da public constructor yok; oluşturma handler'ında TODO bırakıldı.");
    if (updateTodo)
        warnings.Add($"{entity.Name}'de 'Update(...)' metodu ya da public setter'lar yok; güncelleme handler'ında TODO bırakıldı.");

    return Report(a, layout, results, $"{entity.Name} ({entity.Namespace}, Id: {entity.IdType})", warnings, new
    {
        entity.Name,
        entity.Namespace,
        entity.IdType,
        entity.FilePath,
        HasFactory = !createTodo,
        HasUpdate = !updateTodo,
        CreateTodo = createTodo,
        UpdateTodo = updateTodo,
    });
}

int DynamicQuery(Arguments a)
{
    Banner();
    (SolutionLayout layout, EntityModel entity) = SelectEntity(a, 2);
    IReadOnlyList<ChangeResult> results = CrudGenerator
        .PlanDynamicQuery(layout, entity, new CrudOptions { Plural = a.Option("plural"), Secured = !a.Flag("no-secured") })
        .Apply();
    return Report(a, layout, results, $"{entity.Name}: GetListByDynamic", [], new { entity.Name });
}

int Entity(Arguments a)
{
    Banner();
    SolutionLayout layout = Layout(a);
    string name = a.Positional(2) ?? Text($"Yeni {G("entity")} adı (tekil, ör. Brand)");
    if (string.IsNullOrWhiteSpace(name))
        throw new GeneratorException("Entity adı gerekli: canfxgen generate entity Brand");

    string? folder = a.Option("folder") is { Length: > 0 } f ? Path.GetFullPath(f) : null;
    GenerationPlan plan = EntityGenerator.Plan(layout, name, folder, a.Option("id") ?? "Guid");
    IReadOnlyList<ChangeResult> results = plan.Apply();
    return Report(a, layout, results, $"{Names.Pascal(name)} (entity)", ["Taban sınıfı dosyadaki yorumlardan seç, alanları ekle; sonra: canfxgen generate crud " + Names.Pascal(name)], new { Name = Names.Pascal(name) });
}

int Seeder(Arguments a)
{
    if (!a.Json)
        Banner();
    (SolutionLayout layout, EntityModel entity) = SelectEntity(a, 2);

    // IDE diyaloğu: mevcut durum (alanlar, kayıtlar, örnek veri).
    if (a.Flag("describe"))
    {
        SeedDescription description = SeederGenerator.Describe(layout, entity, a.Option("plural"));
        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, solution = layout.Root, seed = description }, json));
        return 0;
    }

    int count = a.Option("count") is { } c ? int.TryParse(c, out int parsed) && parsed > 0 ? parsed : throw new GeneratorException($"--count pozitif bir sayı olmalı: {c}") : 20;
    IReadOnlyList<IReadOnlyDictionary<string, string?>>? records = null;
    if (a.Option("data") is { } dataFile)
    {
        if (!File.Exists(dataFile))
            throw new GeneratorException($"--data dosyası bulunamadı: {dataFile}");
        try
        {
            records = JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(File.ReadAllText(dataFile))?
                .Select(r => (IReadOnlyDictionary<string, string?>)r).ToList() ?? [];
        }
        catch (JsonException exception)
        {
            throw new GeneratorException($"--data dosyası okunamadı (alan → metin değer dizisi olmalı): {exception.Message}");
        }
    }

    var options = new SeederOptions
    {
        Required = !a.Flag("sample-only"),
        Sample = !a.Flag("required-only"),
        HasData = a.Flag("has-data"),
        SampleCount = count,
        Plural = a.Option("plural"),
        Records = records,
        SampleKinds = a.Option("sample-kinds") is { } kinds ? SeederGenerator.ParseKinds(kinds) : null,
    };

    (int order, string reason) = SeederGenerator.SuggestedOrder(layout, entity);
    GenerationPlan plan = SeederGenerator.Plan(layout, entity, options);
    string title = $"{entity.Name}: seed (sıra {order})";
    string[] warnings = [$"Sıra önerisi: {order}. {reason}"];
    if (a.Flag("preview") || a.Flag("dry-run"))
        return PrintPreview(a, layout, plan, title, [.. warnings]);
    return Report(a, layout, plan.Apply(), title, warnings, new { entity.Name, Order = order });
}

// ================================================================ enable / disable

int Toggle(Arguments a, bool enable)
{
    if (!a.Json)
        Banner();
    SolutionLayout layout = Layout(a);

    var mechanisms = new List<Mechanism>();
    string? target = null;
    for (int i = 1; a.Positional(i) is { } word; i++)
    {
        if (MechanismEditor.Parse(word) is { } mechanism)
            mechanisms.Add(mechanism);
        else if (target is null)
            target = word;
        else
            throw new GeneratorException($"Fazla argüman: {word}");
    }

    foreach ((string flag, Mechanism mechanism) in new[] { ("caching", Mechanism.Caching), ("logging", Mechanism.Logging), ("transaction", Mechanism.Transaction), ("secured", Mechanism.Secured), ("dynamic-query", Mechanism.DynamicQuery) })
    {
        if (a.Flag(flag))
            mechanisms.Add(mechanism);
    }

    string verb = enable ? "açılacak" : "kapatılacak";
    if (mechanisms.Count == 0)
    {
        IReadOnlyList<string> chosen = MultiSelect($"Hangi {G("mekanizmalar")} {verb}?", [("caching", false), ("logging", false), ("transaction", false), ("secured", false), ("dynamic-query", false)]);
        mechanisms.AddRange(chosen.Select(c => MechanismEditor.Parse(c)!.Value));
        if (mechanisms.Count == 0)
            throw new GeneratorException("Mekanizma gerekli: canfxgen " + (enable ? "enable" : "disable") + " caching Brand");
    }

    target ??= Text($"Hedef: {G("istek")} (CreateBrandCommand), {G("özellik")} (Brands) ya da {G("entity")} (Brand)");
    var warnings = new List<string>();
    GenerationPlan plan = MechanismEditor.Plan(layout, target, mechanisms, enable, warnings);
    string title = $"{target}: {string.Join(", ", mechanisms.Distinct().Select(MechanismEditor.Name))} {(enable ? "açıldı" : "kapatıldı")}";

    if (a.Flag("preview") || a.Flag("dry-run"))
        return PrintPreview(a, layout, plan, title, warnings);

    return Report(a, layout, plan.Apply(), title, warnings, new { Target = target, Enable = enable, Mechanisms = mechanisms.Distinct() });
}

int MechanismStatusCommand(Arguments a)
{
    SolutionLayout layout = Layout(a);
    string target = a.Positional(1) ?? throw new GeneratorException("Hedef gerekli: canfxgen mechanisms Brand (istek, özellik, entity ya da dosya yolu).");
    MechanismReport report = MechanismEditor.Status(layout, target);
    if (a.Json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ok = true,
            solution = layout.Root,
            target = report.Target,
            singleRequest = report.SingleRequest,
            requests = report.Requests,
            mechanisms = report.Mechanisms.Select(m => new { name = MechanismEditor.CliName(m.Mechanism), enabled = m.Enabled, total = m.Total }),
        }, json));
        return 0;
    }

    Console.WriteLine($"{report.Target} ({report.Requests} istek)");
    foreach (MechanismStatus m in report.Mechanisms)
    {
        string state = m.Total == 0 ? "geçerli değil" : m.Enabled == 0 ? "kapalı" : m.Enabled == m.Total ? "açık" : $"karışık ({m.Enabled}/{m.Total})";
        Console.WriteLine($"  {MechanismEditor.CliName(m.Mechanism),-15} {state}");
    }

    return 0;
}

// ================================================================ add / remove feature

int Feature(Arguments a, bool enable)
{
    if (!a.Json)
        Banner();
    SolutionLayout layout = Layout(a);
    int index = a.Positional(1) is "feature" or "features" ? 2 : 1;
    string name = a.Positional(index) ?? Select($"Hangi {G("özellik")} {(enable ? "eklenecek" : "çıkarılacak")}?", FeaturePatcher.Supported.Select(f => f.ToLowerInvariant()).ToList());
    string feature = FeaturePatcher.Normalize(name);
    if (FeaturePatcher.Unsupported.Contains(feature))
    {
        throw new GeneratorException(
            $"{feature} sonradan {(enable ? "eklenemez" : "çıkarılamaz")}: veritabanı şemasını ya da kimlik akışını değiştirir. " +
            "Yeni bir çözüm oluşturup (canfxgen new) kodu taşımak daha güvenli.");
    }

    var warnings = new List<string>();
    Dictionary<string, string> options = FeaturePatcher.ReadOptions(layout, warnings);
    bool current = options.TryGetValue(feature, out string? value) && value == "true";
    if (current == enable)
        throw new GeneratorException($"{feature} zaten {(enable ? "açık" : "kapalı")}.");

    string temp = Path.Combine(Path.GetTempPath(), "canfxgen-" + Guid.NewGuid().ToString("N")[..8]);
    try
    {
        // Şablon iki kez üretilir: projenin seçenekleriyle (taban) ve özellik değiştirilmiş haliyle (hedef); fark uygulanır.
        string baseDirectory = Path.Combine(temp, "base");
        string targetDirectory = Path.Combine(temp, "target");
        if (!a.Json)
            Console.WriteLine(Dim("Şablon karşılaştırma için üretiliyor ..."));
        RunTemplate(layout.RootNamespace, baseDirectory, options);
        var target = new Dictionary<string, string>(options, StringComparer.Ordinal) { [feature] = enable ? "true" : "false" };
        RunTemplate(layout.RootNamespace, targetDirectory, target);

        GenerationPlan plan = FeaturePatcher.Plan(layout, baseDirectory, targetDirectory, feature, enable, warnings);
        string title = $"{feature} {(enable ? "eklendi" : "çıkarıldı")}";
        if (a.Flag("preview") || a.Flag("dry-run"))
            return PrintPreview(a, layout, plan, title, warnings);

        IReadOnlyList<ChangeResult> results = plan.Apply();
        warnings.Add("Derle ve dene: dotnet build. Yeni ayarlar appsettings.json'da; Development'ta servis gerekiyorsa docker'dan kendiliğinden başlatılır.");
        return Report(a, layout, results, title, warnings, new { Feature = feature, Enable = enable });
    }
    finally
    {
        if (Directory.Exists(temp))
            Directory.Delete(temp, recursive: true);
    }
}

static void RunTemplate(string name, string output, IReadOnlyDictionary<string, string> options)
{
    var arguments = new List<string> { "new", "canfx-monolith", "-n", name, "-o", output };
    arguments.AddRange(FeaturePatcher.TemplateArguments(options));
    var info = new ProcessStartInfo("dotnet", arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    using Process process = Process.Start(info) ?? throw new GeneratorException("dotnet çalıştırılamadı.");
    string stdout = process.StandardOutput.ReadToEnd();
    string stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new GeneratorException(
            "Şablon üretilemedi (karşılaştırma için gerekli). Şablon kurulu mu: dotnet new install CanFx.Templates\n" + (stderr + stdout).Trim());
    }
}

// ================================================================ rate-limit

int RateLimit(Arguments a)
{
    if (!a.Json)
        Banner();
    SolutionLayout layout = Layout(a);
    string policy = a.Positional(1) ?? Text($"Hız sınırı {G("politikası")} (ör. reports; hazır: auth, sensitive)");
    string target = a.Positional(2) ?? Text($"Hangi {G("uç")}: istek (CreateReportCommand), Controller.Action ya da controller/özellik");

    int permit = a.Option("permit") is { } p ? int.TryParse(p, out int value) ? value : throw new GeneratorException($"--permit sayı olmalı: {p}") : 60;
    var options = new RateLimitOptions
    {
        Remove = a.Flag("remove"),
        PermitLimit = permit,
        Window = a.Option("window") ?? "00:01:00",
        PartitionBy = a.Option("partition-by") ?? "UserOrIp",
    };

    var warnings = new List<string>();
    GenerationPlan plan = RateLimitBinder.Plan(layout, policy, target, options, warnings);
    string title = options.Remove ? $"{target}: hız sınırı kaldırıldı" : $"{target}: '{policy}' hız sınırı";
    if (a.Flag("preview") || a.Flag("dry-run"))
        return PrintPreview(a, layout, plan, title, warnings);

    return Report(a, layout, plan.Apply(), title, warnings, new { Policy = policy, Target = target, options.Remove });
}

int PrintPreview(Arguments a, SolutionLayout layout, GenerationPlan plan, string title, IReadOnlyList<string> warnings)
{
    List<FilePreview> previews = plan.Preview().Where(p => p.Kind is ChangeKind.Created or ChangeKind.Overwritten or ChangeKind.Updated or ChangeKind.Deleted or ChangeKind.Failed).ToList();
    if (a.Json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ok = previews.All(p => p.Kind != ChangeKind.Failed),
            solution = layout.Root,
            preview = true,
            warnings,
            changes = previews.Select(p => new { p.Path, p.Kind, Diff = Diff.Unified(p.Before, p.After) }),
        }, json));
        return 0;
    }

    Console.WriteLine($"{title} {Dim("(önizleme: hiçbir dosya değiştirilmedi)")}");
    foreach (FilePreview preview in previews)
    {
        Console.WriteLine();
        Console.WriteLine($"{preview.Kind switch { ChangeKind.Created => "🆕", ChangeKind.Deleted => "🗑 ", ChangeKind.Failed => "❌", _ => "🔼" }} {B(Path.GetRelativePath(layout.Root, preview.Path))}");
        foreach (string line in Diff.Unified(preview.Before, preview.After).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Console.WriteLine(line.StartsWith('+') ? G(line) : line.StartsWith('-') ? R(line) : Dim(line));
    }

    if (previews.Count == 0)
        Console.WriteLine(Dim("Değişiklik yok (zaten istenen durumda)."));
    foreach (string warning in warnings)
        Console.WriteLine($"⚠️  {warning}");
    Console.WriteLine(Dim("\nUygulamak için aynı komutu --preview olmadan çalıştır."));
    return 0;
}

int Request(Arguments a, bool isCommand)
{
    Banner();
    SolutionLayout layout = Layout(a);
    string kind = isCommand ? "komut" : "sorgu";

    string name = a.Positional(2) ?? Text($"Yeni {G(kind)} adı");
    if (string.IsNullOrWhiteSpace(name))
        throw new GeneratorException($"{kind} adı gerekli: canfxgen generate {(isCommand ? "command SyncUser Users" : "query GetUserByEmail Users")}");

    string feature = a.Positional(3) ?? Select($"{Names.Pascal(kind)} hangi {G("özellikte")}?", FeatureGenerator.ListFeatures(layout));

    (bool caching, bool logging, bool transaction, bool secured, _) = Mechanisms(a, includeTransaction: isCommand, includeDynamic: false);
    var options = new RequestOptions
    {
        Caching = caching,
        Logging = logging,
        Transaction = transaction,
        Secured = secured,
        EndpointMethod = a.Option("endpoint-method"),
    };

    GenerationPlan plan = isCommand
        ? FeatureGenerator.PlanCommand(layout, feature, name, options)
        : FeatureGenerator.PlanQuery(layout, feature, name, options);
    IReadOnlyList<ChangeResult> results = plan.Apply();
    return Report(a, layout, results, $"{feature}: {Names.Pascal(name)} ({kind})", [], new { Feature = feature, Name = name });
}

// ================================================================ listeler

int Entities(Arguments a)
{
    SolutionLayout layout = Layout(a);
    IReadOnlyList<(string Name, string FilePath)> entities = EntityReader.List(layout);
    if (a.Json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, solution = layout.Root, entities = entities.Select(e => new { e.Name, e.FilePath }) }, json));
        return 0;
    }

    foreach ((string name, string path) in entities)
        Console.WriteLine($"{name,-30} {Dim(Path.GetRelativePath(layout.Root, path))}");
    return 0;
}

int Features(Arguments a)
{
    SolutionLayout layout = Layout(a);
    IReadOnlyList<string> features = FeatureGenerator.ListFeatures(layout);
    if (a.Json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, solution = layout.Root, features }, json));
        return 0;
    }

    foreach (string feature in features)
        Console.WriteLine(feature);
    return 0;
}

// ================================================================ yardımcılar

SolutionLayout Layout(Arguments a, string? hint = null)
{
    SolutionLayout layout = SolutionLayout.Find(hint ?? Directory.GetCurrentDirectory());
    if (Interactive && !a.Json)
        Console.WriteLine($"Seçili {G("çözüm")}: {B(Path.GetFileName(layout.Root))}.");
    return layout;
}

(SolutionLayout Layout, EntityModel Entity) SelectEntity(Arguments a, int index)
{
    string? target = a.Positional(index);
    SolutionLayout layout = Layout(a, target is not null && target.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? target : null);
    if (target is null)
    {
        IReadOnlyList<(string Name, string FilePath)> entities = EntityReader.List(layout).Where(e => e.Name != "AppUser").ToList();
        if (entities.Count == 0)
            throw new GeneratorException($"Domain'de entity yok ({layout.Domain}). Entity'yi Entities klasörüne yazıp tekrar dene.");
        target = Select($"Hangi {G("entity")}?", entities.Select(e => e.Name).ToList());
    }

    return (layout, EntityReader.Read(layout, target));
}

/// <summary>
/// Mekanizmalar: bayrak verilmişse (-c -l -t -s -d) yalnızca onlar; verilmemişse etkileşimli seçim (hepsi işaretli),
/// etkileşimsiz çalışmada hepsi açık.
/// </summary>
(bool Caching, bool Logging, bool Transaction, bool Secured, bool Dynamic) Mechanisms(Arguments a, bool includeTransaction, bool includeDynamic)
{
    string[] flags = ["caching", "logging", "transaction", "secured", "dynamic-query", "no-mechanisms"];
    if (flags.Any(a.Flag))
        return (a.Flag("caching"), a.Flag("logging"), a.Flag("transaction"), a.Flag("secured"), a.Flag("dynamic-query"));

    if (!Interactive)
        return (true, true, true, true, true);

    var options = new List<(string, bool)> { ("Caching", true), ("Logging", true) };
    if (includeTransaction)
        options.Add(("Transaction", true));
    options.Add(("Secured Operation", true));
    if (includeDynamic)
        options.Add(("Dynamic Query", true));

    IReadOnlyList<string> chosen = MultiSelect($"Hangi {G("mekanizmalar")} kullanılsın?", options);
    return (chosen.Contains("Caching"), chosen.Contains("Logging"), chosen.Contains("Transaction"), chosen.Contains("Secured Operation"), chosen.Contains("Dynamic Query"));
}

int Report(Arguments a, SolutionLayout layout, IReadOnlyList<ChangeResult> results, string title, IReadOnlyList<string> warnings, object details)
{
    bool failed = results.Any(r => r.Kind == ChangeKind.Failed);
    if (a.Json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { ok = !failed, solution = layout.Root, entity = details, warnings, changes = results }, json));
        return failed ? 1 : 0;
    }

    Console.WriteLine(title);

    void Section(string header, Func<ChangeResult, bool> filter, string mark)
    {
        List<ChangeResult> items = results.Where(filter).ToList();
        if (items.Count == 0)
            return;
        Console.WriteLine($"{mark} {G(header)}");
        foreach (ChangeResult item in items)
            Console.WriteLine($"   {B(Path.GetRelativePath(layout.Root, item.Path))}{(item.Note is null ? string.Empty : Dim($"  ({item.Note})"))}");
    }

    Section("Üretilen dosyalar:", r => r.Kind is ChangeKind.Created or ChangeKind.Overwritten, "🆕");
    Section("Güncellenen dosyalar:", r => r.Kind == ChangeKind.Updated, "🔼");
    Section("Atlanan dosyalar (zaten var, değiştirilmedi):", r => r.Kind == ChangeKind.Skipped, "⏭ ");
    Section("Silinen dosyalar:", r => r.Kind == ChangeKind.Deleted, "🗑 ");
    Section("Başarısız:", r => r.Kind == ChangeKind.Failed, "❌");

    foreach (string warning in warnings)
        Console.WriteLine(Warn("⚠ " + warning));

    Console.WriteLine(Dim("Tamamlanacak yerler 'TODO(canfxgen)' ile işaretli (Rider: View → Tool Windows → TODO)."));
    return failed ? 1 : 0;
}

int Fail(string message, bool asJson)
{
    if (asJson)
        Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = message }, json));
    else
        Console.Error.WriteLine("Hata: " + message);
    return 2;
}

static int Print(string text)
{
    Console.WriteLine(text);
    return 0;
}

static string Version() =>
    typeof(CrudGenerator).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

static string Help() => """
    canfxgen — CanFx kod üretici (nArchitecture düzeni)

    ÖRNEKLER:
        canfxgen new MyShop
        canfxgen generate crud Brand
        canfxgen generate command SyncBrand Brands
        canfxgen generate query GetBrandByName Brands
        canfxgen generate dynamic-query Brand
        canfxgen add feature redis --preview
        canfxgen remove feature search
        canfxgen rate-limit reports GetBrandReportQuery --permit 20 --partition-by Tenant
        canfxgen enable caching Brand --preview
        canfxgen disable secured logging CreateBrandCommand

    KOMUTLAR:
        new [Ad]                  CanFx şablonuyla yeni çözüm (veritabanı ve özellikler sorulur)
        generate (g)              Kod üret: crud, command, query, dynamic-query (canfxgen generate --help)
        add / remove feature <Ad> Şablon özelliğini sonradan ekler / çıkarır: redis, search, event-bus, hangfire,
                                  file-storage, realtime, sms, open-telemetry, postman (paket, kayıt, appsettings,
                                  docker-compose birlikte). --preview: değişiklikleri göster, yazma
        rate-limit <Politika> <Uç> Uca hız sınırı bağlar ([EnableRateLimiting]); politika appsettings'te yoksa eklenir.
                                  Uç: istek (CreateReportCommand), Controller.Action, controller ya da özellik.
                                  --permit 60 --window 00:01:00 --partition-by UserOrIp|Ip|Tenant, --remove, --preview
                                  Hazır politikalar: auth (giriş/kayıt), sensitive (kod gönderme, şifre)
        enable / disable          Mekanizmayı sonradan aç / kapat: caching, logging, transaction, secured, dynamic-query
                                  Hedef: istek (CreateBrandCommand), özellik (Brands), entity (Brand) ya da dosya yolu.
                                  --preview: değişiklikleri göster, dosyalara yazma
        mechanisms <Hedef>        Hedefteki mekanizmaların durumu (açık / kapalı / karışık; --json)
        entities                  Domain'deki entity'leri listeler
        features                  Application/Features altındaki özellikleri listeler
        version                   Sürümü yazar

    SEÇENEKLER:
            --json                Makine okunur çıktı, soru sorulmaz (IDE eklentileri için)
        -h, --help                Yardım
        -v, --version             Sürüm
    """;

static string GenerateHelp() => """
    canfxgen generate — kod üret

    ÖRNEKLER:
        canfxgen generate entity Brand                 (Domain/Entities/Brand.cs; taban sınıf seçenekleri dosyada)
        canfxgen generate entity Product --folder src/Shop.Domain/Catalog --id int
        canfxgen generate crud                         (entity ve mekanizmalar sorulur)
        canfxgen generate crud Brand -c -l -t -s -d
        canfxgen generate crud src/Shop.Domain/Entities/Brand.cs --plural Brands
        canfxgen generate seeder Brand                 (zorunlu veri JSON'dan + geliştirme örnek verisi)
        canfxgen generate seeder Currency --required-only --has-data
        canfxgen generate command SyncBrand Brands -e Put
        canfxgen generate query GetBrandByName Brands -c -s
        canfxgen generate dynamic-query Brand

    ÜRETİMLER:
        entity [Ad]                       Domain'de yeni entity; taban sınıf seçenekleri ve ekledikleri alanlar yorumda
                                          (--folder <klasör>: Domain içinde başka klasör, --id Guid|int|long)
        crud [Entity]                     Create/Update/Delete komutları, GetById/GetList(+ByDynamic) sorguları, yanıtlar,
                                          Messages/OperationClaims, MappingProfiles, BusinessRules, repository, servis
                                          (IXService/XManager), EF yapılandırması, controller; DbSet, kayıtlar, yetkiler, çeviri
        seeder [Entity]                   Persistence/Seeding: {Entity}Seeder (her ortamda; Seeding/Data/{çoğul}.json'dan,
                                          eksikler eklenir), {Entity}SampleSeeder (yalnızca geliştirmede, SampleData);
                                          sıra ilişkili entity'lerin seeder'larına göre önerilir
                                          --required-only | --sample-only, --count <N> (örnek sayısı, 20),
                                          --has-data (zorunlu veri JSON yerine EF Core HasData ile migration'a),
                                          --data <kayıtlar.json> (alan → değer dizisi; dosya bu kayıtlarla yazılır),
                                          --sample-kinds "Name=City; Price=Price" (örnek değer türü), --describe
                                          (mevcut durum, JSON), --preview
        command [Ad] [Özellik]            Özelliğe komut (+ yanıt, validator, yetki, controller ucu)
        query [Ad] [Özellik]              Özelliğe sorgu (+ yanıt, validator, yetki, controller ucu)
        dynamic-query [Entity]            CRUD'a sonradan dinamik filtre/sıralama sorgusu

    MEKANİZMALAR (hiçbiri verilmezse sorulur; etkileşimsizde hepsi açık):
        -c, --caching                     Sorgular önbelleğe alınır (kayıt değişince kendiliğinden silinir)
        -l, --logging                     Komutlar loglanır
        -t, --transaction                 Komutlar transaction içinde (kapalıysa handler kendisi kaydeder)
        -s, --secured                     Yetki kontrolü (OperationClaims)
        -d, --dynamic-query               GetListByDynamic (yalnızca crud)

    DİĞER:
        -e, --endpoint-method <Metot>     Controller ucu: Get, Post, Put, Delete (komut: Post, sorgu: Get)
            --plural <Çoğul>              Çoğul ad (varsayılan: İngilizce kurallar)
            --json                        Makine okunur çıktı (IDE eklentileri için)
    """;
