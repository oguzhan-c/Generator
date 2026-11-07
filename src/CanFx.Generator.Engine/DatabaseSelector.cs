using System.Text.RegularExpressions;

namespace CanFx.Generator.Engine;

/// <summary>Şablonda yorum satırı olarak hazır gelen veritabanı sağlayıcıları.</summary>
public enum DatabaseProvider
{
    /// <summary>Henüz seçilmedi (şablonun ilk hali).</summary>
    None,

    PostgreSql,

    SqlServer,

    /// <summary>Elle ayarlanmış başka bir sağlayıcı (SQLite, MySQL ...): canfxgen dokunmaz.</summary>
    Custom,
}

/// <summary>
/// Veritabanı seçimi: şablondaki hazır yorumları (sağlayıcı satırı, docker-compose servisi, bağlantı dizesi,
/// DevServices) açar; diğer sağlayıcı açıksa yorumlar. Paketi eklemez (sürümünü kullanıcı seçer): eksikse eklenecek
/// komutu uyarı olarak verir. Başka bir veritabanı elle ayarlanabilir; o zaman canfxgen dokunmaz.
/// </summary>
public static partial class DatabaseSelector
{
    private sealed record Provider(string Package, string Use, string Compose, string Marker);

    private static readonly Provider Postgres = new("Npgsql.EntityFrameworkCore.PostgreSQL", "UseNpgsql", "postgres", "Host=");
    private static readonly Provider SqlServer = new("Microsoft.EntityFrameworkCore.SqlServer", "UseSqlServer", "sqlserver", "Server=");
    private const string SqlServerPkcs = "System.Security.Cryptography.Pkcs";

    public static DatabaseProvider? Parse(string name) => name.ToLowerInvariant() switch
    {
        "postgres" or "postgresql" or "npgsql" or "pg" => DatabaseProvider.PostgreSql,
        "sqlserver" or "sql-server" or "mssql" => DatabaseProvider.SqlServer,
        _ => null,
    };

    public static string CliName(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.PostgreSql => "postgres",
        DatabaseProvider.SqlServer => "sqlserver",
        DatabaseProvider.Custom => "custom",
        _ => "none",
    };

    /// <summary>Projede açık olan sağlayıcı (PersistenceServiceRegistration.cs'teki satıra göre).</summary>
    public static DatabaseProvider Current(SolutionLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        string path = Registration(layout);
        if (!File.Exists(path))
            return DatabaseProvider.None;
        string text = File.ReadAllText(path);
        if (Active(text, Postgres.Use))
            return DatabaseProvider.PostgreSql;
        if (Active(text, SqlServer.Use))
            return DatabaseProvider.SqlServer;
        return OtherProvider().IsMatch(text) ? DatabaseProvider.Custom : DatabaseProvider.None;
    }

    public static GenerationPlan Plan(SolutionLayout layout, DatabaseProvider provider, List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(warnings);
        if (provider is not (DatabaseProvider.PostgreSql or DatabaseProvider.SqlServer))
            throw new GeneratorException("canfxgen yalnızca PostgreSQL ya da SQL Server'ı açar; başka bir veritabanını elle ayarla " +
                "(PersistenceServiceRegistration.cs → \"Veritabanı\").");
        if (Current(layout) == DatabaseProvider.Custom)
            throw new GeneratorException("Projede elle ayarlanmış başka bir veritabanı sağlayıcısı var; canfxgen ona dokunmaz. " +
                "Değiştirmek için o satırı kaldır ya da elle düzenle.");

        Provider chosen = provider == DatabaseProvider.PostgreSql ? Postgres : SqlServer;
        Provider other = provider == DatabaseProvider.PostgreSql ? SqlServer : Postgres;
        string name = provider == DatabaseProvider.PostgreSql ? "PostgreSQL" : "SQL Server";

        // Paket kullanıcıda: eksikse komutu, eski sağlayıcınınki duruyorsa kaldırma komutunu ver.
        string csproj = Path.Combine(layout.Persistence, Path.GetFileName(layout.Persistence) + ".csproj");
        string project = Path.GetRelativePath(layout.Root, layout.Persistence).Replace('\\', '/');
        string references = File.Exists(csproj) ? File.ReadAllText(csproj) : string.Empty;
        // Yapılması gerekenler sağlayıcı satırının üstüne TODO olarak da yazılır (her çalıştırmada yeniden hesaplanır).
        var todos = new List<string>();
        if (!References(references, chosen.Package))
        {
            warnings.Add($"{name} paketini ekle (sürümünü sen seçersin): dotnet add {project} package {chosen.Package}");
            todos.Add($"{PackageTodo} dotnet add {project} package {chosen.Package} (ekledikten sonra bu satırı sil)");
        }

        // SqlServer paketi eski bir Pkcs getiriyor (NU1902/NU1903; TreatWarningsAsErrors ile derleme durur).
        if (provider == DatabaseProvider.SqlServer && !References(references, SqlServerPkcs))
        {
            warnings.Add($"SQL Server paketinin getirdiği eski System.Security.Cryptography.Pkcs'teki güvenlik açığı için güncelini de ekle: dotnet add {project} package {SqlServerPkcs}");
            todos.Add($"{PackageTodo} dotnet add {project} package {SqlServerPkcs} (SQL Server paketinin getirdiği eski sürümdeki güvenlik açığı için; ekledikten sonra bu satırı sil)");
        }

        // Sağlayıcı değişti ve migration'lar var: onlar eski veritabanına göre üretildi.
        DatabaseProvider previous = Current(layout);
        bool migrations = Directory.Exists(Path.Combine(layout.Persistence, "Migrations"));
        bool switched = previous is not DatabaseProvider.None && previous != provider;
        if (switched && migrations)
            warnings.Add("Migrations klasöründeki migration'lar önceki veritabanına göre üretildi: klasörü silip yeniden oluştur (canfxgen migration add Initial).");
        if (References(references, other.Package))
            warnings.Add($"Eski sağlayıcının paketi duruyor: dotnet remove {project} package {other.Package}");
        if (provider == DatabaseProvider.PostgreSql && References(references, SqlServerPkcs))
            warnings.Add($"SQL Server için eklenen paket de gereksiz: dotnet remove {project} package {SqlServerPkcs}");

        var plan = new GenerationPlan();
        plan.Edit(Registration(layout), $"options.{chosen.Use}(...)", t => RegistrationEdit(t, chosen, other, todos, switched, migrations));
        plan.Edit(Path.Combine(layout.Root, "docker-compose.yml"), $"{chosen.Compose} servisi", t => Compose(t, chosen, other));
        plan.Edit(Path.Combine(layout.WebApi, "appsettings.Development.json"), "bağlantı dizesi ve DevServices", t => DevelopmentSettings(t, chosen, other));
        return plan;
    }

    private static string Registration(SolutionLayout layout) => Path.Combine(layout.Persistence, "PersistenceServiceRegistration.cs");

    private static bool Active(string text, string use) => Regex.IsMatch(text, $@"(?m)^[ \t]*options\.{use}\(");

    // ------------------------------------------------------------------ dosyalar

    private static bool References(string csproj, string package) =>
        Regex.IsMatch(csproj, $@"(?m)^[ \t]*<PackageReference Include=""{Regex.Escape(package)}""");

    private const string PackageTodo = "// TODO(canfxgen): veritabanı paketi:";
    private const string MigrationTodo = "// TODO(canfxgen): veritabanı değişti: Migrations klasöründeki migration'lar önceki veritabanına göre; klasörü silip yeniden oluştur (canfxgen migration add Initial), sonra bu satırı sil.";

    private static InjectResult RegistrationEdit(string text, Provider chosen, Provider other, List<string> todos, bool switched, bool migrations)
    {
        text = Regex.Replace(text, $@"(?m)^([ \t]*)// (options\.{chosen.Use}\()", "$1$2");
        text = Regex.Replace(text, $@"(?m)^([ \t]*)(options\.{other.Use}\()", "$1// $2");
        text = TodoLine().Replace(text, string.Empty);
        text = ThrowBlock().Replace(text, string.Empty);

        // Önceki çalıştırmanın paket TODO'ları kalkar (paket eklendiyse bir daha yazılmaz); migration TODO'su migration'lar silinince.
        bool hadMigrationTodo = text.Contains(MigrationTodo, StringComparison.Ordinal);
        text = Regex.Replace(text, $@"(?m)^[ \t]*{Regex.Escape(PackageTodo)}[^\n]*\n", string.Empty);
        text = Regex.Replace(text, $@"(?m)^[ \t]*{Regex.Escape(MigrationTodo)}[^\n]*\n", string.Empty);
        var lines = new List<string>(todos);
        if (migrations && (switched || hadMigrationTodo))
            lines.Add(MigrationTodo);

        Match use = Regex.Match(text, $@"(?m)^([ \t]*)options\.{chosen.Use}\(");
        if (!use.Success)
            return new InjectResult(text, $"'// options.{chosen.Use}(...)' satırı bulunamadı; elle ekle");

        string indent = use.Groups[1].Value;
        string block = string.Concat(lines.Select(l => indent + l + "\n"));
        return new InjectResult(text[..use.Index] + block + text[use.Index..], null);
    }

    private static InjectResult Compose(string text, Provider chosen, Provider other)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        List<string> lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        bool found = false;

        for (int i = 0; i < lines.Count; i++)
        {
            // Seçilen servis: "  # postgres:" ve altındaki "  #   ..." satırları açılır.
            if (lines[i] == $"  # {chosen.Compose}:")
            {
                found = true;
                lines[i] = $"  {chosen.Compose}:";
                for (int j = i + 1; j < lines.Count && lines[j].StartsWith("  #   ", StringComparison.Ordinal); j++)
                    lines[j] = "  " + lines[j][4..];
            }
            else if (lines[i] == $"  {chosen.Compose}:")
            {
                found = true;
            }
            // Diğer servis açıksa yorumlanır.
            else if (lines[i] == $"  {other.Compose}:")
            {
                lines[i] = $"  # {other.Compose}:";
                for (int j = i + 1; j < lines.Count && lines[j].StartsWith("    ", StringComparison.Ordinal); j++)
                    lines[j] = "  # " + lines[j][2..];
            }
            else if (lines[i] == "  # db-data:")
            {
                lines[i] = "  db-data:";
            }
            // Uygulama konteyneri (--profile app): bağlantı dizesi ve bağımlılık.
            else if (Regex.IsMatch(lines[i], $@"^      # ConnectionStrings__[^:]+: {Regex.Escape(chosen.Marker)}"))
            {
                lines[i] = "      " + lines[i][8..];
            }
            else if (Regex.IsMatch(lines[i], $@"^      ConnectionStrings__[^:]+: {Regex.Escape(other.Marker)}"))
            {
                lines[i] = "      # " + lines[i][6..];
            }
            else if (lines[i] == "    # depends_on:")
            {
                lines[i] = "    depends_on:";
            }
            else if (DependsOnLine().Match(lines[i]) is { Success: true })
            {
                lines[i] = $"      {chosen.Compose}: {{ condition: service_healthy }}";
            }
        }

        return new InjectResult(string.Join(newline, lines), found ? null : $"docker-compose.yml'de '{chosen.Compose}' servisi bulunamadı; elle ekle");
    }

    private static InjectResult DevelopmentSettings(string text, Provider chosen, Provider other)
    {
        // Yalnızca ConnectionStrings bölümünde: seçilen satır açılır, diğeri yorumlanır, boş değer silinir.
        Match section = ConnectionStrings().Match(text);
        if (section.Success)
        {
            string body = section.Groups["body"].Value;
            body = Regex.Replace(body, $@"(?m)^([ \t]*)// (""[^""]+"": ""{Regex.Escape(chosen.Marker)})", "$1$2");
            body = Regex.Replace(body, $@"(?m)^([ \t]*)(""[^""]+"": ""{Regex.Escape(other.Marker)})", "$1// $2");
            body = EmptyConnectionString().Replace(body, string.Empty);
            text = text[..section.Groups["body"].Index] + body + text[(section.Groups["body"].Index + section.Groups["body"].Length)..];
        }

        text = Regex.Replace(text, $@"(?m)^([ \t]*)// (""Database"": \{{ ""Compose"": ""{chosen.Compose}"")", "$1$2");
        text = Regex.Replace(text, $@"(?m)^([ \t]*)(""Database"": \{{ ""Compose"": ""{other.Compose}"")", "$1// $2");

        bool active = Regex.IsMatch(text, $@"(?m)^[ \t]*""[^""]+"": ""{Regex.Escape(chosen.Marker)}");
        return new InjectResult(text, active ? null : "bağlantı dizesi satırı bulunamadı; ConnectionStrings'e elle yaz");
    }

    [GeneratedRegex(@"(?m)^[ \t]*options\.Use(Sqlite|MySql|MySQL|Oracle|InMemoryDatabase|Cosmos|Firebird|Jet)\(")]
    private static partial Regex OtherProvider();

    [GeneratedRegex(@"(?m)^[ \t]*// TODO\(canfxgen\): Veritabanı sağlayıcısını seç[^\n]*\n")]
    private static partial Regex TodoLine();

    [GeneratedRegex(@"[ \t]*throw new InvalidOperationException\(\s*\$?""Veritabanı ayarlanmadı.*?açıklamasına bak\.""\);[ \t]*\r?\n", RegexOptions.Singleline)]
    private static partial Regex ThrowBlock();

    [GeneratedRegex(@"^    #?\s*(postgres|sqlserver): \{ condition: service_healthy \}.*$")]
    private static partial Regex DependsOnLine();

    [GeneratedRegex(@"""ConnectionStrings"":\s*\{(?<body>.*?)\n  \}", RegexOptions.Singleline)]
    private static partial Regex ConnectionStrings();

    // Bölümün son satırı olabilir (arkasında satır sonu yok): önceki satır sonuyla birlikte silinir.
    [GeneratedRegex(@"\r?\n[ \t]*""[^""]+"": """"[ \t]*(?=\r?\n|\z)")]
    private static partial Regex EmptyConnectionString();
}
