using System.Data.Common;
using CanFx.DevServices;

namespace CanAppTemplate.WebAPI.Development;

/// <summary>
/// Geliştirme servisleri: uygulama Development'ta açılırken kullandığı servisler (veritabanı ve appsettings'te açılmış
/// olanlar) bilgisayarda çalışıyorsa onlar kullanılır, çalışmıyorsa docker-compose.yml'den yalnızca o servis başlatılır.
/// Servis başına mod appsettings.Development.json'da: <c>"DevServices": { "Services": { "Database": "Auto" } }</c>
/// (Auto | Docker | Local | Off).
/// </summary>
public static class DevServicesSetup
{
    public static IServiceCollection AddDevServices(this IServiceCollection services, IConfiguration config)
    {
        return services.AddCanDevServices(o =>
        {
            config.GetSection("DevServices").Bind(o, b => b.ErrorOnUnknownConfiguration = true);
            // Veritabanı: bağlantı dizesinden (Host=... → docker-compose'daki "postgres", Server=... → "sqlserver").
            AddDatabase(o, config.GetConnectionString("CanAppTemplate"));

            // Yalnızca appsettings'te açılmış servisler (adres boşsa uygulama yerel karşılığını kullanır, docker gerekmez).
            if (string.Equals(config["Mail:Provider"], "Smtp", StringComparison.OrdinalIgnoreCase))
                o.Add("Mail", "mailpit", $"{config["Mail:Smtp:Host"] ?? "localhost"}:{config["Mail:Smtp:Port"] ?? "1025"}");
#if (Redis)
            if (config["Redis:ConnectionString"] is { Length: > 0 } redis)
                o.Add("Redis", "redis", redis.Split(',')[0]);
#endif
#if (Search)
            if (config["Search:Elasticsearch:Url"] is { Length: > 0 } elasticsearch && Uri.TryCreate(elasticsearch, UriKind.Absolute, out Uri? esUri))
                o.Add("Search", "elasticsearch", $"{esUri.Host}:{esUri.Port}");
#endif
#if (EventBus)
            if (config["EventBus:RabbitMq:ConnectionString"] is { Length: > 0 } rabbit && Uri.TryCreate(rabbit, UriKind.Absolute, out Uri? rabbitUri))
                o.Add("EventBus", "rabbitmq", $"{rabbitUri.Host}:{(rabbitUri.Port > 0 ? rabbitUri.Port : 5672)}");
#endif
        });
    }

    /// <summary>
    /// Bağlantı dizesindeki sunucu bu bilgisayarsa (localhost) veritabanı servisi tanımlanır: <c>Host=</c> PostgreSQL
    /// (docker-compose: postgres, 5432), <c>Server=</c> SQL Server (sqlserver, 1433). Uzak sunucuya docker başlatılmaz.
    /// </summary>
    private static void AddDatabase(DevServicesOptions options, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var builder = new DbConnectionStringBuilder();
        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            return; // bağlantı dizesi değil (ör. dosya yolu)
        }

        (string service, string? server, int port) = builder.TryGetValue("Host", out object? host)
            ? ("postgres", host as string, 5432)
            : builder.TryGetValue("Server", out object? srv) || builder.TryGetValue("Data Source", out srv)
                ? ("sqlserver", srv as string, 1433)
                : (string.Empty, null, 0);
        if (string.IsNullOrWhiteSpace(server))
            return;

        // "localhost,1433" (SQL Server) / Host=localhost;Port=5432 (PostgreSQL)
        server = server.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase);
        string[] parts = server.Split(',', 2);
        string hostName = parts[0].Trim();
        if (parts.Length > 1 && int.TryParse(parts[1], out int p))
            port = p;
        else if (builder.TryGetValue("Port", out object? portValue) && int.TryParse(portValue?.ToString(), out int p2))
            port = p2;

        if (hostName is "localhost" or "127.0.0.1" or "::1" or ".")
            options.Add("Database", service, $"{(hostName == "." ? "localhost" : hostName)}:{port}");
    }
}
