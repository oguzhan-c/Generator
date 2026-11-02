using CanFx.Security.Entities;
using CanFx.Security.Tokens;

namespace CanAppTemplate.Application.Services.AuthService;

/// <summary>
/// Giriş/yenileme sonucu. Token'lar yanıt gövdesine konmaz; WebApi bunları HttpOnly cookie olarak yazar.
/// </summary>
public sealed record AuthResult(AccessToken AccessToken, RefreshTokenValue RefreshToken, UserProfileDto User, string SessionId);
