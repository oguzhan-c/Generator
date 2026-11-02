using CanFx.Mediator;
using CanFx.Security.Entities;
using CanFx.Security.Events;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;
using CanAppTemplate.Application.Features.Account.Rules;
using CanAppTemplate.Application.Features.Auth.Rules;
using CanAppTemplate.Application.Services.AuthService;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Features.Account.Commands.EnableOtp;

public sealed class EnableOtpCommandHandler : IRequestHandler<EnableOtpCommand, RecoveryCodesDto>
{
    private readonly TwoFactorCodeVerifier _verifier;
    private readonly RecoveryCodeIssuer _recoveryCodes;
    private readonly ISecurityEvents _events;
    private readonly AuthBusinessRules _authBusinessRules;
    private readonly AccountBusinessRules _accountBusinessRules;

    public EnableOtpCommandHandler(
        TwoFactorCodeVerifier verifier,
        RecoveryCodeIssuer recoveryCodes,
        ISecurityEvents events,
        AuthBusinessRules authBusinessRules,
        AccountBusinessRules accountBusinessRules)
    {
        _verifier = verifier;
        _recoveryCodes = recoveryCodes;
        _events = events;
        _authBusinessRules = authBusinessRules;
        _accountBusinessRules = accountBusinessRules;
    }

    public async Task<RecoveryCodesDto> Handle(EnableOtpCommand request, CancellationToken cancellationToken)
    {
        AppUser user = await _authBusinessRules.CurrentUserShouldExist(cancellationToken: cancellationToken);
        _accountBusinessRules.OtpCodeShouldBeValid(await _verifier.VerifyOtpAsync(user.Id, request.Code, requireVerified: false, cancellationToken) is not null);

        user.SetAuthenticator(AuthenticatorType.Otp);
        await _events.PublishAsync(new SecurityEvent(SecurityEventTypes.TwoFactorEnabled, user.Id.ToString()) { Reason = "otp" }, cancellationToken);
        return await _recoveryCodes.RegenerateAsync(user.Id, cancellationToken);
    }
}
