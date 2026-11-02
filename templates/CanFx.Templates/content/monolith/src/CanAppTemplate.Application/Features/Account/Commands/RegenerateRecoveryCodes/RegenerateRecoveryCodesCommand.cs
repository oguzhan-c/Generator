using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Account.Commands.RegenerateRecoveryCodes;

/// <summary>Kurtarma kodlarını yeniler (eskileri geçersiz olur). Yalnızca iki adımlı doğrulama açıkken.</summary>
public sealed record RegenerateRecoveryCodesCommand(string? Password) : IRequest<RecoveryCodesDto>, ISecuredRequest, ITransactionalRequest, ILoggableRequest;
