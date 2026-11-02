using CanFx.Application;
using CanFx.Mediator;

namespace CanAppTemplate.Application.Features.Auth.Commands.ResetPassword;

/// <summary>
/// Kodla yeni şifre belirler. Güvenlik damgası yenilenir: tüm oturumlar (çalınmış olabilecekler dahil) kapanır;
/// kullanıcı yeni şifresiyle giriş yapar. Hesap kilitliyse kilit kalkar.
/// </summary>
public sealed record ResetPasswordCommand(string Email, string Code, string NewPassword) : IRequest, ILoggableRequest;
