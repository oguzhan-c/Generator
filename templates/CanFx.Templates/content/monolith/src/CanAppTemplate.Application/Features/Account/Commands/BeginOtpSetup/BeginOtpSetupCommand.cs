using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;
using CanAppTemplate.Application.Features.Account.Commands.EnableOtp;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Account.Commands.BeginOtpSetup;

/// <summary>
/// Authenticator uygulaması kurulumunun ilk adımı: yeni gizli anahtar üretilir (henüz aktif değil). Kullanıcı QR kodu
/// okutup uygulamadaki ilk kodu <see cref="EnableOtpCommand"/> ile girince iki adımlı giriş açılır.
/// </summary>
public sealed record BeginOtpSetupCommand : IRequest<OtpSetupDto>, ISecuredRequest, ITransactionalRequest, ILoggableRequest;
