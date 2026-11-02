using CanFx.Mediator;
using CanFx.Persistence.Repositories;
using CanAppTemplate.Application.Services.AuthService;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Features.Auth.Commands.ResendVerificationCode;

public sealed class ResendVerificationCodeCommandHandler : IRequestHandler<ResendVerificationCodeCommand>
{
    private readonly IRepository<AppUser, IdentityKey> _users;
    private readonly EmailVerification _emailVerification;

    public ResendVerificationCodeCommandHandler(IRepository<AppUser, IdentityKey> users, EmailVerification emailVerification)
    {
        _users = users;
        _emailVerification = emailVerification;
    }

    public async Task Handle(ResendVerificationCodeCommand request, CancellationToken cancellationToken)
    {
        string normalizedEmail = request.Email.Trim().ToUpperInvariant();
        AppUser? user = await _users.GetAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken: cancellationToken);

        if (user is { EmailConfirmed: false })
            await _emailVerification.SendCodeAsync(user, VerificationPurpose.ConfirmEmail, cancellationToken);
    }
}
