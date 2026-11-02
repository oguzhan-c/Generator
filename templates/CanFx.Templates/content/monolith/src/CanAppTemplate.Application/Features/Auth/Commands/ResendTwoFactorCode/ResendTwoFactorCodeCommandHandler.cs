using CanFx.Mediator;
using CanAppTemplate.Application.Features.Auth.Rules;
using CanAppTemplate.Application.Services.AuthService;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Features.Auth.Commands.ResendTwoFactorCode;

public sealed class ResendTwoFactorCodeCommandHandler : IRequestHandler<ResendTwoFactorCodeCommand>
{
    private readonly EmailVerification _emailVerification;
    private readonly AuthBusinessRules _authBusinessRules;

    public ResendTwoFactorCodeCommandHandler(EmailVerification emailVerification, AuthBusinessRules authBusinessRules)
    {
        _emailVerification = emailVerification;
        _authBusinessRules = authBusinessRules;
    }

    public async Task Handle(ResendTwoFactorCodeCommand request, CancellationToken cancellationToken)
    {
        AppUser user = await _authBusinessRules.TwoFactorChallengeShouldBeValid(request.Challenge, cancellationToken);
        _authBusinessRules.TwoFactorShouldUseEmail(user);
        await _emailVerification.SendCodeAsync(user, VerificationPurpose.SignIn, cancellationToken);
    }
}
