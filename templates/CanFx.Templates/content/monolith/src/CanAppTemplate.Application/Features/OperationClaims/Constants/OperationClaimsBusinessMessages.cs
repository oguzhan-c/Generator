using CanFx.Application.Authorization;

namespace CanAppTemplate.Application.Features.OperationClaims.Constants;

/// <summary>
/// Yetki hata kodları: ProblemDetails <c>code</c> alanı (istemciyle sözleşme) ve çeviri anahtarı. Metinler kodda
/// değil, <c>Application/Localization/identity.{dil}.json</c>'da; yer tutucular (<c>{max}</c>) <c>exception.With("max", değer)</c> ile doldurulur.
/// </summary>
public static class OperationClaimsBusinessMessages
{
    /// <summary>CanFx kodu; metni Application/Localization/identity.*.json'da.</summary>
    public const string OperationClaimNotExists = AccessGrantErrorCodes.UnknownPermission;

    /// <summary>CanFx kodu; metni Application/Localization/identity.*.json'da.</summary>
    public const string OperationClaimDeprecated = AccessGrantErrorCodes.DeprecatedPermission;
}
