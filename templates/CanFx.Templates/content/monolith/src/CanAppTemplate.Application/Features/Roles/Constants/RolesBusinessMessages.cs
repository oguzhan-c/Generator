using CanFx.Application.Authorization;

namespace CanAppTemplate.Application.Features.Roles.Constants;

/// <summary>
/// Rol hata kodları: ProblemDetails <c>code</c> alanı (istemciyle sözleşme) ve çeviri anahtarı. Metinler kodda
/// değil, <c>Features/Roles/Localization/roles.{dil}.json</c>'da; yer tutucular (<c>{max}</c>) <c>exception.With("max", değer)</c> ile doldurulur.
/// </summary>
public static class RolesBusinessMessages
{
    public const string RoleNotExists = "roles.role_not_exists";

    public const string RoleNameExists = "roles.role_name_exists";

    /// <summary>CanFx kodu; metni Application/Localization/identity.*.json'da.</summary>
    public const string RoleInUse = AccessGrantErrorCodes.RoleInUse;

    public const string HostOnly = "roles.host_only";
}
