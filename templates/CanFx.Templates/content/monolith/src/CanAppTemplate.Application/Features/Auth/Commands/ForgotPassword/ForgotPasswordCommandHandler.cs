using CanFx.Mediator;
using CanFx.Persistence.Repositories;
using CanFx.Security.Events;
using CanAppTemplate.Application.Services.AuthService;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Features.Auth.Commands.ForgotPassword;

public sealed class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand>
{
    private readonly IRepository<AppUser, IdentityKey> _users;
    private readonly EmailVerification _emailVerification;
    private readonly ISecurityEvents _events;

    public ForgotPasswordCommandHandler(IRepository<AppUser, IdentityKey> users, EmailVerification emailVerification, ISecurityEvents events)
    {
        _users = users;
        _emailVerification = emailVerification;
        _events = events;
    }

    public async Task Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        string normalizedEmail = request.Email.Trim().ToUpperInvariant();
        AppUser? user = await _users.GetAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken: cancellationToken);
        if (user is null)
            return;

        await _emailVerification.SendCodeAsync(user, VerificationPurpose.ResetPassword, cancellationToken);
        await _events.PublishAsync(new SecurityEvent(SecurityEventTypes.PasswordResetRequested, user.Id.ToString()), cancellationToken);
    }
}
