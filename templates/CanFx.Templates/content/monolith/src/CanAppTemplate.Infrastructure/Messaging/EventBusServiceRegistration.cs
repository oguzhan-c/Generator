using CanFx.EventBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Infrastructure.Messaging;

/// <summary>
/// Event bus: outbox'taki entegrasyon event'leri süreç içi taşıyıcıyla bu uygulamadaki handler'lara verilir (broker yok;
/// outbox sayesinde kayıp olmaz). Servisler arası mesajlaşma için bir broker ekle: <see cref="AddEventBus"/> içindeki TODO.
/// </summary>
public static class EventBusServiceRegistration
{
    /// <returns>Gerçek bir broker'a bağlıysa <see langword="true"/>.</returns>
    public static bool AddEventBus(this IServiceCollection services, IConfiguration config)
    {
        services.AddCanEventBus(typeof(CanAppTemplate.Domain.Entities.AppUser).Assembly, typeof(CanAppTemplate.Application.ApplicationServiceRegistration).Assembly);

        // TODO(canfxgen): Event bus broker'sız kuruldu: event'ler yalnızca bu uygulamanın içinde dağıtılır. Başka servislerle
        // mesajlaşmak için BİR broker seç (Kafka, RabbitMQ ya da Azure Service Bus) ve şu adımları uygula:
        //
        //   1) Paket: Directory.Packages.props ve Infrastructure .csproj içindeki TODO'da seçtiğin satırı aç.
        //   2) Bu dosya: en üste using'leri ekle, aşağıdaki "_ = config; return false;" satırlarının yerine seçtiğinin bloğunu yaz.
        //        using CanFx.Persistence.DependencyInjection;
        //        using CanAppTemplate.Persistence.Contexts;
        //
        //      Kafka (using CanFx.EventBus.Kafka;):
        //        if (config["EventBus:Kafka:BootstrapServers"] is not { Length: > 0 })
        //            return false;                                  // ayar boşsa süreç içi taşıyıcı devam eder
        //        services.AddCanInbox<BaseDbContext>();             // tekrar gelen mesajı ayıklar (tablolar zaten var)
        //        services.AddCanKafkaTransport(o => config.GetSection("EventBus:Kafka").Bind(o, b => b.ErrorOnUnknownConfiguration = true));
        //        return true;
        //
        //      RabbitMQ (using CanFx.EventBus.RabbitMQ;):
        //        if (config["EventBus:RabbitMq:ConnectionString"] is not { Length: > 0 })
        //            return false;
        //        services.AddCanInbox<BaseDbContext>();
        //        services.AddCanRabbitMqTransport(o => config.GetSection("EventBus:RabbitMq").Bind(o, b => b.ErrorOnUnknownConfiguration = true));
        //        return true;
        //
        //      Azure Service Bus (using CanFx.EventBus.AzureServiceBus;):
        //        if (config["EventBus:AzureServiceBus:ConnectionString"] is not { Length: > 0 }
        //            && config["EventBus:AzureServiceBus:FullyQualifiedNamespace"] is not { Length: > 0 })
        //            return false;
        //        services.AddCanInbox<BaseDbContext>();
        //        services.AddCanAzureServiceBusTransport(o => config.GetSection("EventBus:AzureServiceBus").Bind(o, b => b.ErrorOnUnknownConfiguration = true));
        //        return true;
        //        (Kimlikle bağlanmak için: FullyQualifiedNamespace + o.Credential = new Azure.Identity.DefaultAzureCredential())
        //
        //   3) appsettings.json: "EventBus" bölümü (TODO'daki örneği aç). Gizli değerler (şifre, bağlantı dizesi) user-secrets'a:
        //        dotnet user-secrets set "EventBus:RabbitMq:ConnectionString" "amqp://..."
        //   4) Yerel geliştirme (Kafka / RabbitMQ): docker-compose.yml'deki servisi ve appsettings.Development.json'daki
        //      adres ile DevServices satırını aç; uygulama açılırken docker'dan başlatılır.
        //
        // Yeni migration gerekmez: outbox / inbox tabloları zaten var.
        _ = config;
        return false;
    }
}
