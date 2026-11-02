using CanFx.Security.Entities;
using Microsoft.EntityFrameworkCore;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Services.AuthService;

public static class AppUserQueryExtensions
{
    /// <summary>Token ve profil için gereken roller + yetkiler (rollerin ve doğrudan verilenlerin).</summary>
    public static IQueryable<AppUser> WithRolesAndPermissions(this IQueryable<AppUser> query) =>
        query
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role!).ThenInclude(r => r.OperationClaims).ThenInclude(rc => rc.OperationClaim)
            .Include(u => u.OperationClaims).ThenInclude(uc => uc.OperationClaim)
            .AsSplitQuery();
}
