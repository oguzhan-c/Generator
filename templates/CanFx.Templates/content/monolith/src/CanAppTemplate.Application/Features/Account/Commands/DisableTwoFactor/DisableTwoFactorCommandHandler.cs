using CanFx.Mediator;
using CanFx.Persistence.Repositories;
using CanFx.Security.Entities;
using CanFx.Security.Events;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;
using CanAppTemplate.Application.Features.Auth.Rules;
using CanAppTemplate.Application.Services.AuthService;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Features.Account.Commands.DisableTwoFactor;

public sealed class DisableTwoFactorCommandHandler : IRequestHandler<DisableTwoFactorCommand>
{
    private readonly IRepository<OtpAuthenticator<IdentityKey>, IdentityKey> _otpAuthenticators;
    private readonly CanFx.Security.Hashing.IPasswordHasher _passwordHasher;
    private readonly RecoveryCodeIssuer _recoveryCodes;
    private readonly ISecurityEvents _events;
    private readonly AuthBusinessRules _authBusinessRules;

    public DisableTwoFactorCommandHandler(
        IRepository<OtpAuthenticator<IdentityKey>, IdentityKey> otpAuthenticators,
        CanFx.Security.Hashing.IPasswordHasher passwordHasher,
        RecoveryCodeIssuer recoveryCodes,
        ISecurityEvents events,
        AuthBusinessRules authBusinessRules)
    {
        _otpAuthenticators = otpAuthenticators;
        _passwordHasher = passwordHasher;
        _recoveryCodes = recoveryCodes;
        _events = events;
        _authBusinessRules = authBusinessRules;
    }

    public async Task Handle(DisableTwoFactorCommand request, CancellationToken cancellationToken)
    {
        AppUser user = await _authBusinessRules.CurrentUserShouldExist(cancellationToken: cancellationToken);
        _authBusinessRules.PasswordShouldMatch(
            user.PasswordHash is not null
            && _passwordHasher.Verify(request.Password, user.PasswordHash) != CanFx.Security.Hashing.PasswordVerificationResult.Failed);

        if (await _otpAuthenticators.GetAsync(a => a.UserId == user.Id, cancellationToken: cancellationToken) is { } otp)
            _otpAuthenticators.Delete(otp, permanent: true);

        await _recoveryCodes.DeleteAllAsync(user.Id, cancellationToken);
        user.SetAuthenticator(AuthenticatorType.None);
        await _events.PublishAsync(new SecurityEvent(SecurityEventTypes.TwoFactorDisabled, user.Id.ToString()), cancellationToken);
    }
}
