using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Application.Features.Account.Commands.DisableTwoFactor;

/// <summary>İki adımlı doğrulamayı kapatır; güvenlik için şifre tekrar istenir. Kurtarma kodları silinir.</summary>
public sealed record DisableTwoFactorCommand(string Password) : IRequest, ISecuredRequest, ITransactionalRequest, ILoggableRequest;
