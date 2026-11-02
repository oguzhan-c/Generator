using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Auth.Commands.CompleteTwoFactorLogin;

/// <summary>
/// İki adımlı girişin ikinci adımı: authenticator uygulamasındaki ya da e-postaya gelen 6 haneli kod, ya da cihaz
/// kaybolduysa kurtarma kodlarından biri. <see cref="Challenge"/> sunucunun şifreleyip cookie'de sakladığı bilgidir;
/// istemci değiştiremez.
/// </summary>
public sealed record CompleteTwoFactorLoginCommand(TwoFactorChallenge Challenge, string Code, bool IsRecoveryCode, ClientInfo Client)
    : IRequest<AuthResult>, ILoggableRequest;
