using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Application.Features.Account.Commands.UnlinkExternalLogin;

/// <summary>
/// Bağlı dış hesabı kaldırır. Hesaba girmenin tek yolu buysa kaldırılmaz. Bağlama, sağlayıcıya yönlendirme
/// gerektirdiği için WebApi'deki <c>/api/auth/external/{provider}/link</c> ile yapılır.
/// </summary>
public sealed record UnlinkExternalLoginCommand(string Provider) : IRequest, ISecuredRequest, ITransactionalRequest, ILoggableRequest;
