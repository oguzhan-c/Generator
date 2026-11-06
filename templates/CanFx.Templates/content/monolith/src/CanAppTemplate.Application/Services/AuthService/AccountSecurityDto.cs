#if (TwoFactor)
using CanFx.Security.Entities;
#endif

namespace CanAppTemplate.Application.Services.AuthService;

/// <summary>Hesap güvenliği sayfasının özeti (projede açık olan özelliklere göre).</summary>
/// <param name="HasPassword">Şifresi var mı (dış sağlayıcıyla açılan hesapların şifresi olmayabilir).</param>
#if (TwoFactor)
/// <param name="TwoFactor">Girişte şifreden sonra istenen ikinci adım (<c>None</c>, <c>Email</c>, <c>Otp</c>).</param>
/// <param name="RecoveryCodesLeft">Kullanılmamış kurtarma kodu sayısı (azalınca yenilemesi önerilir).</param>
#endif
#if (Passkey)
/// <param name="PasskeysEnabled">Sunucuda passkey ayarlı mı (<c>Security:Passkey</c>).</param>
#endif
public sealed record AccountSecurityDto(
    string Email,
    bool EmailConfirmed,
    bool HasPassword
#if (TwoFactor)
    , AuthenticatorType TwoFactor
    , int RecoveryCodesLeft
#endif
#if (Passkey)
    , bool PasskeysEnabled
    , IReadOnlyList<PasskeyDto> Passkeys
#endif
#if (ExternalLogin)
    , IReadOnlyList<ExternalLoginDto> ExternalLogins
#endif
    );
