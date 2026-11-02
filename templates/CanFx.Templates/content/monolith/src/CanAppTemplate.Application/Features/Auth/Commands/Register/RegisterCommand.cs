using CanFx.Application;
using CanFx.Mediator;

namespace CanAppTemplate.Application.Features.Auth.Commands.Register;

/// <summary>
/// Üye ol: müşteri hesabı açılır ve e-postaya 6 haneli doğrulama kodu gönderilir. E-posta doğrulanana kadar giriş
/// yapılamaz.
/// </summary>
public sealed record RegisterCommand(string Email, string Password, string FirstName, string LastName, string? PhoneNumber, bool MarketingConsent)
    : IRequest<RegisteredResponse>, ITransactionalRequest, ILoggableRequest;
