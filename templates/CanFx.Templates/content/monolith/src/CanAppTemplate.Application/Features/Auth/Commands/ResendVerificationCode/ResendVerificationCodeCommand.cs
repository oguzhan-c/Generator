using CanFx.Application;
using CanFx.Mediator;

namespace CanAppTemplate.Application.Features.Auth.Commands.ResendVerificationCode;

/// <summary>Yeni doğrulama kodu gönderir. Hesabın var olup olmadığını belli etmemek için her zaman başarılı döner.</summary>
public sealed record ResendVerificationCodeCommand(string Email) : IRequest, ITransactionalRequest;
