using CanFx.Domain.Auditing;
using CanFx.Security.Entities;
using CanAppTemplate.Domain.Identity;

namespace CanAppTemplate.Domain.Entities;

/// <summary>
/// Uygulamanın kullanıcısı: yalnızca alanlar. Kayıt, profil ve rol işlemleri Application katmanında
/// (<c>Application/Services/Users/AppUserService.cs</c>).
/// </summary>
/// <remarks>
/// Taban sınıf seçenekleri (birini seç; değiştirince yeni bir migration ekle):
/// <code>
/// User&lt;IdentityKey&gt;              (varsayılan) Kimlik alanları: Email, UserName, PasswordHash, EmailConfirmed,
///                                   SecurityStamp, AuthenticatorType, AccessFailedCount, LockoutEnd, PasskeyUserHandle;
///                                   ilişkiler: UserRoles, OperationClaims, Logins;
///                                   denetim: CreatedAt/By, UpdatedAt/By, IsDeleted, DeletedAt/By (otomatik doldurulur).
/// UserAggregateRoot&lt;IdentityKey&gt;   Yukarıdakilerin hepsi + domain event (RaiseDomainEvent(...)); event'ler kayıt
///                                   sırasında yayınlanır (outbox açıksa onunla). Kullanıcı değişikliklerine başka
///                                   modüller tepki verecekse (hoş geldin e-postası, CRM eşitleme ...) bunu seç.
/// </code>
/// Yeni alan eklediğinde sütun ayarlarını Persistence/EntityConfigurations/AppUserConfiguration.cs'e yaz.
/// </remarks>
[Audited]
public sealed class AppUser : User<IdentityKey>
{
    public const int NameMaxLength = 50;
    public const int PhoneMaxLength = 20;

    private AppUser() { } // EF Core

    public AppUser(string email)
        : base(IdentityKeys.New(), email) { }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    /// <summary>Kampanya e-postası / SMS izni.</summary>
    public bool MarketingConsent { get; set; }
#if (MultiTenancy)

    /// <summary>
    /// Kullanıcının tenant'ı. Girişte token'a <c>tenant_id</c> olarak yazılır; tenant'a ait tablolar bu tenant'a göre
    /// süzülür. Boşsa kullanıcı host (platform) kullanıcısıdır.
    /// </summary>
    public Guid? TenantId { get; set; }
#endif

    // TODO(canfxgen): Uygulamaya özel kullanıcı alanlarını (ör. avatar, dil tercihi) buraya ekle.
}
