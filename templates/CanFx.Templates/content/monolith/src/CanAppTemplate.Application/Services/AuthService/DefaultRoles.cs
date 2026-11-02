using CanFx.Security.Entities;
using CanFx.Persistence.Repositories;
using CanAppTemplate.Domain.Constants;

namespace CanAppTemplate.Application.Services.AuthService;

internal static class DefaultRoles
{
    /// <summary>Seed eksikse bu bir kurulum hatasıdır (beklenmeyen): exception olarak kalır.</summary>
    public static async Task<Role<IdentityKey>> GetAsync(IRepository<Role<IdentityKey>, IdentityKey> roles, CancellationToken cancellationToken) =>
        await roles.GetAsync(r => r.Name == Roles.User, cancellationToken: cancellationToken)
        ?? throw new InvalidOperationException("User rolü bulunamadı; seed çalıştı mı?");
}
