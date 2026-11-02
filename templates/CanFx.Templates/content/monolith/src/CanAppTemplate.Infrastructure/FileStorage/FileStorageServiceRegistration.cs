using CanFx.FileStorage;
using CanFx.FileStorage.AzureBlob;
using CanFx.FileStorage.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Infrastructure.FileStorage;

/// <summary>Dosyalar: <c>Storage:Provider</c> = Local | S3 | Azure. Dosyalar varsayılan olarak tenant klasörüne yazılır.</summary>
public static class FileStorageServiceRegistration
{
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration config)
    {
        string provider = config["Storage:Provider"] is { Length: > 0 } value ? value : "Local";
        switch (provider.ToUpperInvariant())
        {
            case "S3":
                // SeaweedFS / MinIO / AWS / R2; anahtar user-secrets'ta: "Storage:S3:SecretAccessKey"
                services.AddCanS3FileStorage(o => config.GetSection("Storage:S3").Bind(o, b => b.ErrorOnUnknownConfiguration = true));
                break;
            case "AZURE":
                // Azurite: "UseDevelopmentStorage=true"; gerçek hesap anahtarı user-secrets'ta
                services.AddCanAzureBlobFileStorage(o => config.GetSection("Storage:Azure").Bind(o, b => b.ErrorOnUnknownConfiguration = true));
                break;
            case "LOCAL":
                services.AddCanLocalFileStorage(o => o.RootPath = config["Storage:Local:RootPath"] is { Length: > 0 } root ? root : "files");
                break;
            default:
                throw new InvalidOperationException($"Storage:Provider geçersiz: '{provider}'. Geçerli değerler: Local, S3, Azure.");
        }

        return services;
    }
}
