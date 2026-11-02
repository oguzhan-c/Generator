using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using CanAppTemplate.Domain.Constants;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Persistence.EntityConfigurations;

/// <summary>Core'un kullanıcı yapılandırmasına (tablo, e-posta index'i ...) uygulamaya özgü alanları ekler.</summary>
internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.Property(u => u.FirstName).HasMaxLength(AppUser.NameMaxLength).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(AppUser.NameMaxLength).IsRequired();
        builder.Property(u => u.PhoneNumber).HasMaxLength(AppUser.PhoneMaxLength);
#if (MultiTenancy)
        builder.HasIndex(u => u.TenantId);
#endif

        // TODO(canfxgen): AppUser'a eklediğin alanların sütun ayarlarını (uzunluk, zorunluluk, index) buraya yaz.
    }
}
