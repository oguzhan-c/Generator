#if (Postgres || SqlServer)
using System.Data.Common;
#endif
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
#if (Postgres)
            AddFromConnectionString(o, "Database", "postgres", config.GetConnectionString("CanAppTemplate"), defaultPort: 5432);
#elif (SqlServer)
            AddFromConnectionString(o, "Database", "sqlserver", config.GetConnectionString("CanAppTemplate"), defaultPort: 1433);
#endif

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
#if (Postgres || SqlServer)

    /// <summary>Bağlantı dizesindeki sunucu yerelse (localhost) servis tanımlanır; uzak sunucuya docker başlatılmaz.</summary>
    private static void AddFromConnectionString(DevServicesOptions options, string name, string composeService, string? connectionString, int defaultPort)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        string? server = builder.TryGetValue("Host", out object? host) ? host as string
            : builder.TryGetValue("Server", out object? srv) ? srv as string
            : null;
        if (string.IsNullOrWhiteSpace(server))
            return;

        // "localhost,1433" (SQL Server) / Host=localhost;Port=5432 (PostgreSQL)
        server = server.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase);
        string[] parts = server.Split(',', 2);
        string hostName = parts[0].Trim();
        int port = parts.Length > 1 && int.TryParse(parts[1], out int p) ? p
            : builder.TryGetValue("Port", out object? portValue) && int.TryParse(portValue?.ToString(), out int p2) ? p2
            : defaultPort;

        if (hostName is "localhost" or "127.0.0.1" or "::1" or ".")
            options.Add(name, composeService, $"{(hostName == "." ? "localhost" : hostName)}:{port}");
    }
#endif
}
