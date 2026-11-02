using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Auth.Commands.PasskeyLogin;

/// <summary>
/// Passkey ile giriş (şifresiz). Kullanıcı adı sorulmaz: tarayıcı/cihaz kayıtlı passkey'lerden birini önerir. Passkey
/// kendi başına çok faktörlü sayıldığı için (cihaz + biyometri/PIN) ayrıca ikinci adım istenmez.
/// </summary>
/// <param name="Options">Sunucunun ilk adımda ürettiği ve sakladığı seçenekler (challenge tek kullanımlık).</param>
public sealed record PasskeyLoginCommand(AuthenticatorAssertionRawResponse Response, AssertionOptions Options, ClientInfo Client)
    : IRequest<AuthResult>, ILoggableRequest;
