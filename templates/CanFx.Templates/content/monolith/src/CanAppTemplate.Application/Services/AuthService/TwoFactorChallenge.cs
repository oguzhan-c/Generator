using CanFx.Security.Entities;

namespace CanAppTemplate.Application.Services.AuthService;

/// <summary>
/// Şifre doğrulandı ama ikinci adım gerekiyor. İstemciye gönderilmez: WebApi şifreleyip kısa ömürlü HttpOnly cookie'ye
/// yazar, ikinci adımda geri okur. <see cref="SecurityStamp"/> değiştiyse (şifre/2FA değişti) geçersizdir.
/// </summary>
public sealed record TwoFactorChallenge(IdentityKey UserId, string SecurityStamp, AuthenticatorType Method);
