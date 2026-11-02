using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Auth.Commands.VerifyEmail;

/// <summary>E-postadaki kodu doğrular; başarılıysa kullanıcı doğrudan giriş yapmış olur.</summary>
public sealed record VerifyEmailCommand(string Email, string Code, ClientInfo Client) : IRequest<AuthResult>, ILoggableRequest;
