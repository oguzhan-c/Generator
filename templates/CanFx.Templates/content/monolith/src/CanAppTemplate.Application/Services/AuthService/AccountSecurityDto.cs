using CanFx.Security.Entities;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Application.Services.AuthService;

/// <summary>Hesap güvenliği sayfasının özeti.</summary>
/// <param name="HasPassword">Şifresi var mı (dış sağlayıcıyla açılan hesapların şifresi olmayabilir).</param>
/// <param name="TwoFactor">Girişte şifreden sonra istenen ikinci adım (<c>None</c>, <c>Email</c>, <c>Otp</c>).</param>
/// <param name="RecoveryCodesLeft">Kullanılmamış kurtarma kodu sayısı (azalınca yenilemesi önerilir).</param>
/// <param name="PasskeysEnabled">Sunucuda passkey ayarlı mı (<c>Security:Passkey</c>).</param>
public sealed record AccountSecurityDto(
    string Email,
    bool EmailConfirmed,
    bool HasPassword,
    AuthenticatorType TwoFactor,
    int RecoveryCodesLeft,
    bool PasskeysEnabled,
    IReadOnlyList<PasskeyDto> Passkeys,
    IReadOnlyList<ExternalLoginDto> ExternalLogins);
