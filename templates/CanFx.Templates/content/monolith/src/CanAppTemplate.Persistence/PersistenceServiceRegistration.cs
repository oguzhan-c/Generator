#if (MultiTenancy)
using CanFx.MultiTenancy;
#endif
#if (AuditTrail)
using CanFx.Persistence.AuditTrail;
#endif
using CanFx.Persistence.Database;
using CanFx.Persistence.DependencyInjection;
#if (MultiTenancy)
using CanFx.Persistence.MultiTenancy;
#endif
using CanFx.Persistence.RequestLogging;
using CanFx.Persistence.Retention;
using CanFx.Persistence.Seeding;
#if (Reporting)
using CanFx.Reporting;
using CanFx.Reporting.EntityFrameworkCore;
#endif
using CanFx.Security.Entities;
using CanFx.Security.EntityFrameworkCore;
using CanFx.Security.Events;
#if (Settings)
using CanFx.Settings;
using CanFx.Settings.EntityFrameworkCore;
#endif
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
#if (MultiTenancy)
using CanAppTemplate.Application.Services.Repositories;
#endif
using CanAppTemplate.Domain.Constants;
using CanAppTemplate.Domain.Entities;
using CanAppTemplate.Persistence.Seeding;
using CanAppTemplate.Persistence.Contexts;
#if (MultiTenancy)
using CanAppTemplate.Persistence.Repositories;
#endif

namespace CanAppTemplate.Persistence;

public static class PersistenceServiceRegistration
{
    /// <summary>
    /// EF Core (DbContext, repository'ler, unit of work), güvenlik depoları (oturum, cihaz, güvenlik olayı) ve seed.
    /// </summary>
    /// <remarks>appsettings: <c>ConnectionStrings:CanAppTemplate</c>, <c>Seed</c>, <c>QueryDiagnostics</c>, <c>DataRetention</c>.</remarks>
    public static IServiceCollection AddPersistenceServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Geliştirmede appsettings.Development.json'daki yerel (docker) bağlantı dizesi kullanılır. Gerçek ortamın bağlantı
        // dizesi appsettings'e YAZILMAZ: ortam değişkeni (ConnectionStrings__CanAppTemplate) ya da user-secrets.
        string connectionString =
            configuration.GetConnectionString("CanAppTemplate") ?? throw new InvalidOperationException("ConnectionStrings:CanAppTemplate ayarı eksik.");
#if (MultiTenancy)

        // Tenant'lar appsettings'teki "MultiTenancy:Tenants" listesinden okunur. Kendi veritabanı (ConnectionString) olan
        // tenant ona, olmayan ortak veritabanına bağlanır ve verisi TenantId filtresiyle ayrılır.
        // TODO(canfxgen): Tenant'lar veritabanında tutulacaksa listeyi boş bırak ve kendi ITenantStore'unu kaydet.
        services.AddCanMultiTenancy(o =>
        {
            o.DefaultConnectionString = connectionString;
            configuration.GetSection("MultiTenancy:Tenants").Bind(o.Tenants);
        });
#endif

        services.AddCanPersistence<BaseDbContext>(
#if (MultiTenancy)
            (serviceProvider, options) =>
            {
                string tenantConnectionString = serviceProvider.GetRequiredService<ITenantConnectionStringResolver>().Resolve();
#else
            (_, options) =>
            {
                string tenantConnectionString = connectionString;
#endif
                // Aggregate'ler arası ilişkiler navigation'sız olabilir; filtreli zorunlu ilişki uyarısı burada geçersiz.
                options.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

#if (Postgres)
                options.UseNpgsql(tenantConnectionString, npgsql => npgsql.EnableRetryOnFailure());
#elif (SqlServer)
                options.UseSqlServer(tenantConnectionString, sql => sql.EnableRetryOnFailure());
#else
                options.UseSqlite(tenantConnectionString);
#endif
                // Başka bir veritabanına geçmek için: paketi Directory.Packages.props'ta ve Persistence projesinde değiştir,
                // yukarıdaki satırı değiştir, docker-compose.yml'deki servisi ve appsettings.Development.json'daki
                // bağlantı dizesini güncelle, eski migration'ları sil (sağlayıcıya özeldir).
                //   PostgreSQL (Npgsql.EntityFrameworkCore.PostgreSQL)    : options.UseNpgsql(tenantConnectionString, npgsql => npgsql.EnableRetryOnFailure());
                //   SQL Server (Microsoft.EntityFrameworkCore.SqlServer)  : options.UseSqlServer(tenantConnectionString, sql => sql.EnableRetryOnFailure());
                //   SQLite     (Microsoft.EntityFrameworkCore.Sqlite)     : options.UseSqlite(tenantConnectionString);
            }
        );

#if (AuditTrail)
        services.AddCanAuditTrail();
#endif
#if (EventBus)
        services.AddCanOutbox<BaseDbContext>(o => configuration.GetSection("Outbox").Bind(o, b => b.ErrorOnUnknownConfiguration = true));
#endif
        services.AddCanEntityCacheInvalidation();
        services.AddCanQueryDiagnostics(o => configuration.GetSection("QueryDiagnostics").Bind(o, b => b.ErrorOnUnknownConfiguration = true));

        // Güvenlik: oturumlar, kayıtlı cihazlar, damga doğrulayıcı ve güvenlik olaylarının veritabanı kaydı.
        services.AddCanUserSessions<BaseDbContext, IdentityKey>();
        services.AddCanTrustedDevices<BaseDbContext, IdentityKey>();
        services.AddCanSecurityStampValidator<BaseDbContext, AppUser, IdentityKey>();
        services.AddCanUserAuthorizationStore<BaseDbContext, AppUser, IdentityKey>(); // rol / yetkiler her istekte (önbellekli) veritabanından
        services.AddCanSecurityEventStore<BaseDbContext>();

        var seed = new SeedOptions();
        configuration.GetSection("Seed").Bind(seed, b => b.ErrorOnUnknownConfiguration = true);
        services.AddSingleton(seed);
        services.AddCanDataSeeders(typeof(PersistenceServiceRegistration).Assembly);
#if (Settings)

        // Ayar değerleri veritabanında (tanımlar Application'da: RuntimeSettings).
        services.AddCanSettings(s => s.UseEntityFrameworkStore<BaseDbContext>());
#endif
#if (Reporting)

        // Raporlar: veri kaynakları ve kayıtlı raporlar (uçlar WebApi'de: /api/reporting).
        // TODO(canfxgen): Rapor veri kaynaklarını ekle: .AddSource("orders", sp => sp.GetRequiredService<BaseDbContext>().Orders.Select(...), o => o.Field(...))
        services.AddCanReporting().UseEntityFrameworkStore<BaseDbContext>();
#endif
#if (MultiTenancy)

        services.AddScoped<ICrossTenantReader, CrossTenantReader>();
#endif
        // Entity repository'leri (canfxgen her entity için bir satır ekler).
        // canfxgen:repositories

        AddDataRetention(services, configuration.GetSection("DataRetention"));
        return services;
    }

    /// <summary>İstek aktivite logunu veritabanına yazar (WebApi'de <c>RequestActivityLog:Enabled</c> açıksa çağrılır).</summary>
    public static IServiceCollection AddRequestActivityStore(this IServiceCollection services) =>
        services.AddCanRequestActivityStore<BaseDbContext>();

    /// <summary>
    /// Veri saklama: VARSAYILAN KAPALI (kayıtların tamamı saklanır). <c>DataRetention:Enabled = true</c> ile açılır;
    /// süreler gün olarak ayarlanır, <c>DryRun</c> ile önce yalnızca sayılır.
    /// </summary>
    private static void AddDataRetention(IServiceCollection services, IConfigurationSection section)
    {
        if (!section.GetValue("Enabled", false))
            return;

        int Days(string key, int fallback) => section.GetValue($"{key}Days", fallback);

        services.AddCanDataRetention<BaseDbContext>(o =>
        {
            o.DryRun = section.GetValue("DryRun", true);
#if (AuditTrail)
            o.Keep<AuditLog>(TimeSpan.FromDays(Days("AuditLogs", 365)), a => a.Timestamp);
#endif
            o.Keep<SecurityEventRecord>(TimeSpan.FromDays(Days("SecurityEvents", 180)), e => e.OccurredAt);
            o.Keep<SecurityEventRecord>(TimeSpan.FromDays(Days("SuccessfulLogins", 30)), e => e.OccurredAt, where: e => e.Type == SecurityEventTypes.LoginSucceeded);
            o.Keep<RequestActivityRecord>(TimeSpan.FromDays(Days("RequestActivities", 90)), a => a.Timestamp);
            o.Keep<RefreshToken<IdentityKey>>(TimeSpan.FromDays(Days("ExpiredRefreshTokens", 30)), t => t.ExpiresAt);
            o.Keep<TrustedDevice<IdentityKey>>(TimeSpan.FromDays(Days("RevokedDevices", 90)), d => d.RevokedAt);
            // TODO(canfxgen): Zamanla birikip silinebilecek uygulama kayıtlarını (bildirim, log ...) buraya ekle.
        });
    }

    /// <summary>
    /// Veritabanını <c>Database:StartupMode</c>'a göre hazırlar ve başlangıç verisini yükler:
    /// <c>Migrate</c> (migration'ları uygular, yoksa şemayı oluşturur), <c>Recreate</c> (siler ve yeniden oluşturur;
    /// yalnızca Development'ta ve yerel veritabanında), <c>None</c> (dokunmaz). Geliştirmede örnek veri de eklenir
    /// (<c>IDataSeeder.DevelopmentOnly</c>).
    /// </summary>
    public static async Task InitializeDatabaseAsync(
        this IServiceProvider services,
        DatabaseStartupMode mode,
        bool isDevelopment,
        CancellationToken cancellationToken = default)
    {
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("CanAppTemplate.Database");
#if (MultiTenancy)
        await services.InitializeTenantDatabasesAsync<BaseDbContext>(
            (db, ct) => DatabaseStartup.PrepareAsync(db, mode, isDevelopment, logger, ct),
            cancellationToken);
#else
        await using (AsyncServiceScope scope = services.CreateAsyncScope())
        {
            BaseDbContext db = scope.ServiceProvider.GetRequiredService<BaseDbContext>();
            await DatabaseStartup.PrepareAsync(db, mode, isDevelopment, logger, cancellationToken);
        }
#endif

        if (mode != DatabaseStartupMode.None)
            await services.SeedDataAsync(includeDevelopmentData: isDevelopment, cancellationToken: cancellationToken);
    }
}
