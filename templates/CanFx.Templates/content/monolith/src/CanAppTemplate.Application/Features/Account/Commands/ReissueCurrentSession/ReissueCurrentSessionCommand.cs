using CanFx.Application;
using CanFx.Mediator;
using CanFx.Security.Entities;
using CanAppTemplate.Application.Features.Auth.Commands.RefreshToken;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Account.Commands.ReissueCurrentSession;

/// <summary>
/// Güvenlik damgasını değiştiren bir işlemden (şifre, 2FA, dış hesap) hemen sonra, AYNI istekte, işlemi yapan oturuma
/// yeni damgalı token'lar verir: diğer cihazlar kapanır, bu cihaz açık kalır. Mevcut refresh token aktif olmalı ve bu
/// kullanıcıya ait olmalı; damgası eski olduğu için normal yenileme (<see cref="RefreshTokenCommand"/>) burada kullanılamaz.
/// </summary>
public sealed record ReissueCurrentSessionCommand(string? RefreshToken, ClientInfo Client) : IRequest<AuthResult>, ISecuredRequest;
