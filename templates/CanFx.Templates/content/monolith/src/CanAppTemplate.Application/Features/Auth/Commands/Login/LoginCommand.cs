using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.Auth.Commands.CompleteTwoFactorLogin;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Auth.Commands.Login;

/// <summary>
/// E-posta + şifre ile giriş. İki adımlı doğrulama açıksa oturum açılmaz, ikinci adım istenir
/// (<see cref="CompleteTwoFactorLoginCommand"/>). Başarılı/başarısız girişler, kilitlenme ve yeni cihazdan giriş
/// güvenlik olayı olarak yayınlanır.
/// </summary>
public sealed record LoginCommand(string Email, string Password, ClientInfo Client) : IRequest<LoggedResponse>, ILoggableRequest;
