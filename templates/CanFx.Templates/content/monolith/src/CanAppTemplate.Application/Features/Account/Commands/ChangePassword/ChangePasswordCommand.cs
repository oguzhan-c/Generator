using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;
using CanAppTemplate.Application.Features.Account.Commands.ReissueCurrentSession;

namespace CanAppTemplate.Application.Features.Account.Commands.ChangePassword;

/// <summary>
/// Şifre değiştirir ya da (dış sağlayıcıyla açılmış, şifresiz hesapta) şifre belirler. Güvenlik damgası yenilenir:
/// diğer cihazlardaki oturumlar kapanır. Bu oturum WebApi'de yeni token'larla sürer (<see cref="ReissueCurrentSessionCommand"/>).
/// </summary>
public sealed record ChangePasswordCommand(string? CurrentPassword, string NewPassword) : IRequest, ISecuredRequest, ITransactionalRequest, ILoggableRequest;
