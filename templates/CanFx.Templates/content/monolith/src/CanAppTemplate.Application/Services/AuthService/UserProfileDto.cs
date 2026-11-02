using CanFx.Security.Entities;
using CanAppTemplate.Domain.Constants;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Services.AuthService;

public sealed record UserProfileDto(
    IdentityKey Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool EmailConfirmed,
    bool MarketingConsent,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions)
{
    /// <remarks>Roller ve yetkiler yüklenmiş olmalı (<see cref="AppUserQueryExtensions.WithRolesAndPermissions"/>).</remarks>
    public static UserProfileDto From(AppUser user) =>
        new(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            user.EmailConfirmed,
            user.MarketingConsent,
            RolesOf(user),
            user.GetPermissionNames()
        );

    /// <remarks><c>UserRoles.Role</c> yüklenmiş olmalı.</remarks>
    public static string[] RolesOf(AppUser user) =>
        user.UserRoles.Where(r => r.Role is not null).Select(r => r.Role!.Name).Order(StringComparer.Ordinal).ToArray();
}
