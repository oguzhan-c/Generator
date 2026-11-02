using CanFx.Application;
using CanFx.Mediator;

namespace CanAppTemplate.Application.Features.Auth.Commands.ForgotPassword;

/// <summary>
/// "Şifremi unuttum": e-postaya kod gönderilir. Hesabın var olup olmadığı belli olmasın diye her zaman başarılı döner.
/// </summary>
public sealed record ForgotPasswordCommand(string Email) : IRequest, ITransactionalRequest, ILoggableRequest;
