using CanFx.Resilience;
using CanFx.Resilience.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Infrastructure.Resilience;

/// <summary>
/// Dayanıklılık: dış servis çağrıları geçici hatalarda (ağ hatası, zaman aşımı, 5xx, 408, 429) yeniden denenir, sürekli hata
/// veren servise bir süre istek gönderilmez (devre kesici). Ayarlar appsettings → <c>Resilience</c>.
/// </summary>
/// <remarks>
/// (1) HTTP: uygulamanın TÜM <c>HttpClient</c>'larına (IHttpClientFactory ile oluşturulanlar: SMS, SendGrid, kendi
/// istemcilerin) standart handler eklenir; ayrıca bir şey yazman gerekmez. Tekrar denenen istekte gövde yeniden okunur:
/// tek seferlik akış (stream) gönderen istemcide retry'ı kapat.
/// (2) Kod: HTTP dışı işler (ör. bir SDK çağrısı) için adlı pipeline:
/// <code>
/// ResiliencePipeline pipeline = provider.GetPipeline(ResilienceServiceRegistration.DefaultPipeline);
/// await pipeline.ExecuteAsync(async ct =&gt; await client.SendAsync(..., ct), cancellationToken);
/// </code>
/// </remarks>
public static class ResilienceServiceRegistration
{
    /// <summary>Kod içinde kullanılan varsayılan pipeline'ın adı (<see cref="ResiliencePipelineProvider"/>).</summary>
    public const string DefaultPipeline = "default";

    public static IServiceCollection AddResilience(this IServiceCollection services, IConfiguration config)
    {
        IConfigurationSection http = config.GetSection("Resilience:Http");
        services.ConfigureHttpClientDefaults(client => client.AddCanStandardResilienceHandler(o =>
        {
            o.TotalRequestTimeout = http.GetValue("TotalRequestTimeout", o.TotalRequestTimeout);
            o.AttemptTimeout = http.GetValue("AttemptTimeout", o.AttemptTimeout);
            o.Retry.MaxRetryAttempts = http.GetValue("MaxRetryAttempts", o.Retry.MaxRetryAttempts);
            o.Retry.Delay = http.GetValue("RetryDelay", o.Retry.Delay);
            o.CircuitBreaker.BreakDuration = http.GetValue("BreakDuration", o.CircuitBreaker.BreakDuration);
        }));

        IConfigurationSection code = config.GetSection("Resilience:Default");
        services.AddCanResiliencePipeline(DefaultPipeline, (pipeline, _) => pipeline
            .AddTimeout(code.GetValue("TotalTimeout", TimeSpan.FromSeconds(30)))
            .AddRetry(new RetryOptions
            {
                MaxRetryAttempts = code.GetValue("MaxRetryAttempts", 3),
                Delay = code.GetValue("RetryDelay", TimeSpan.FromMilliseconds(500)),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
            }));

        // TODO(canfxgen): Kritik bir dış servis için ayrı pipeline gerekiyorsa (ör. ödeme: daha az deneme, devre kesici)
        // burada services.AddCanResiliencePipeline("payments", ...) ile ekle.
        return services;
    }
}
