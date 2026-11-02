using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Account.Commands.EnableOtp;

/// <summary>
/// Kurulumu tamamlar: uygulamadaki ilk kod doğruysa girişte artık bu uygulamanın kodu istenir. Kurtarma kodları
/// üretilip bir kez döndürülür.
/// </summary>
public sealed record EnableOtpCommand(string Code) : IRequest<RecoveryCodesDto>, ISecuredRequest, ITransactionalRequest, ILoggableRequest;
