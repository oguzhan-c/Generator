using CanFx.EventBus;
#if (RabbitMq)
using CanFx.EventBus.RabbitMQ;
#elif (Kafka)
using CanFx.EventBus.Kafka;
#else
using CanFx.EventBus.AzureServiceBus;
#endif
using CanFx.Persistence.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CanAppTemplate.Persistence.Contexts;

namespace CanAppTemplate.Infrastructure.Messaging;

/// <summary>
#if (RabbitMq)
/// Event bus: outbox'taki entegrasyon event'leri bus'a verilir. <c>EventBus:RabbitMq:ConnectionString</c> doluysa
/// RabbitMQ (servisler arası, inbox ile tam bir kez işleme); yoksa süreç içi taşıyıcı.
#elif (Kafka)
/// Event bus: outbox'taki entegrasyon event'leri bus'a verilir. <c>EventBus:Kafka:BootstrapServers</c> doluysa Kafka
/// (servisler arası, inbox ile tam bir kez işleme); yoksa süreç içi taşıyıcı.
#else
/// Event bus: outbox'taki entegrasyon event'leri bus'a verilir. <c>EventBus:AzureServiceBus:ConnectionString</c> (ya da
/// <c>FullyQualifiedNamespace</c>) doluysa Azure Service Bus (servisler arası, inbox ile tam bir kez işleme); yoksa süreç
/// içi taşıyıcı. Yerel karşılığı (docker) yok: Development'ta boş bırak ya da user-secrets'a gerçek bir ad alanı yaz.
#endif
/// </summary>
public static class EventBusServiceRegistration
{
    /// <returns>Gerçek bir broker'a bağlıysa <see langword="true"/>.</returns>
    public static bool AddEventBus(this IServiceCollection services, IConfiguration config)
    {
        services.AddCanEventBus(typeof(CanAppTemplate.Domain.Entities.AppUser).Assembly, typeof(CanAppTemplate.Application.ApplicationServiceRegistration).Assembly);

#if (RabbitMq)
        if (config["EventBus:RabbitMq:ConnectionString"] is not { Length: > 0 })
            return false;

        services.AddCanInbox<BaseDbContext>();
        services.AddCanRabbitMqTransport(o => config.GetSection("EventBus:RabbitMq").Bind(o, b => b.ErrorOnUnknownConfiguration = true));
#elif (Kafka)
        if (config["EventBus:Kafka:BootstrapServers"] is not { Length: > 0 })
            return false;

        services.AddCanInbox<BaseDbContext>();
        services.AddCanKafkaTransport(o => config.GetSection("EventBus:Kafka").Bind(o, b => b.ErrorOnUnknownConfiguration = true));
#else
        if (config["EventBus:AzureServiceBus:ConnectionString"] is not { Length: > 0 }
            && config["EventBus:AzureServiceBus:FullyQualifiedNamespace"] is not { Length: > 0 })
            return false;

        // TODO(canfxgen): Bağlantı dizesi yerine kimlikle (Managed Identity) bağlanacaksan FullyQualifiedNamespace'i doldur ve
        // aşağıda o.Credential = new Azure.Identity.DefaultAzureCredential() ver (Azure.Identity paketi).
        services.AddCanInbox<BaseDbContext>();
        services.AddCanAzureServiceBusTransport(o => config.GetSection("EventBus:AzureServiceBus").Bind(o, b => b.ErrorOnUnknownConfiguration = true));
#endif
        return true;
    }
}
