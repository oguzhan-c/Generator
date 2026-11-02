using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Auth.Commands.ResendTwoFactorCode;

/// <summary>E-posta ile ikinci adımda yeni kod gönderir.</summary>
public sealed record ResendTwoFactorCodeCommand(TwoFactorChallenge Challenge) : IRequest, ITransactionalRequest;
