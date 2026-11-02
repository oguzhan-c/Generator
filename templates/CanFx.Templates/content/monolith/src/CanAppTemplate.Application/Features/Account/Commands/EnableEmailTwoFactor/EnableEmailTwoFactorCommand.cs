using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Account.Commands.EnableEmailTwoFactor;

public sealed record EnableEmailTwoFactorCommand : IRequest<RecoveryCodesDto>, ISecuredRequest, ITransactionalRequest, ILoggableRequest;
