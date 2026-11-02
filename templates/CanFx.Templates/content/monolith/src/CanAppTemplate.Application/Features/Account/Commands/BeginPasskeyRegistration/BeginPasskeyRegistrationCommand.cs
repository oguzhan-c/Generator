using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Application.Features.Account.Commands.BeginPasskeyRegistration;

/// <summary>Passkey kaydının ilk adımı: tarayıcıya verilecek seçenekler (WebApi bunları kısa süre saklar).</summary>
public sealed record BeginPasskeyRegistrationCommand : IRequest<CredentialCreateOptions>, ISecuredRequest;
