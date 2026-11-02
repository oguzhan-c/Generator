using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Account.Commands.CompletePasskeyRegistration;

/// <summary>Passkey kaydını tamamlar: tarayıcının yanıtı ilk adımdaki seçeneklerle doğrulanır ve passkey saklanır.</summary>
public sealed record CompletePasskeyRegistrationCommand(AuthenticatorAttestationRawResponse Response, CredentialCreateOptions Options, string? Name)
    : IRequest<PasskeyDto>, ISecuredRequest, ITransactionalRequest, ILoggableRequest;
