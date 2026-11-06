using CanFx.Mediator;
using CanFx.Persistence.Repositories;
using CanFx.Security.Entities;
using CanFx.Security.Events;
using CanFx.Security.Hashing;
using CanFx.Security.Sessions;
using CanAppTemplate.Application.Features.Auth.Rules;
using CanAppTemplate.Application.Services.AuthService;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Features.Auth.Commands.Login;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, LoggedResponse>
{
    private readonly IRepository<AppUser, IdentityKey> _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AuthTokenIssuer _tokenIssuer;
#if (EmailTwoFactor)
    private readonly EmailVerification _emailVerification;
#endif
    private readonly IUserSessionService _sessions;
    private readonly ISecurityEvents _events;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly AuthBusinessRules _authBusinessRules;

    public LoginCommandHandler(
        IRepository<AppUser, IdentityKey> users,
        IPasswordHasher passwordHasher,
        AuthTokenIssuer tokenIssuer,
#if (EmailTwoFactor)
        EmailVerification emailVerification,
#endif
        IUserSessionService sessions,
        ISecurityEvents events,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        AuthBusinessRules authBusinessRules)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _tokenIssuer = tokenIssuer;
#if (EmailTwoFactor)
        _emailVerification = emailVerification;
#endif
        _sessions = sessions;
        _events = events;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _authBusinessRules = authBusinessRules;
    }

    public async Task<LoggedResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        string normalizedEmail = request.Email.Trim().ToUpperInvariant();
        AppUser? user = await _users.GetAsync(
            u => u.NormalizedEmail == normalizedEmail,
            include: q => q.WithRolesAndPermissions(),
            cancellationToken: cancellationToken
        );
        DateTimeOffset now = _timeProvider.GetUtcNow();

        if (user?.PasswordHash is null)
        {
            // Kullanıcı yokken de aynı süreyi harca: yanıt süresinden e-postanın kayıtlı olup olmadığı anlaşılmasın.
            _ = _passwordHasher.Hash(request.Password);
            await _events.PublishAsync(new SecurityEvent(SecurityEventTypes.LoginFailed, user?.Id.ToString()) { Reason = user is null ? "unknown_user" : "no_password" }, cancellationToken);
        }

        _authBusinessRules.CredentialsShouldBeValid(user?.PasswordHash is not null, user: null, now);

        if (user.IsLockedOut(now))
            await _events.PublishAsync(new SecurityEvent(SecurityEventTypes.LoginFailed, user.Id.ToString()) { Reason = "locked_out" }, cancellationToken);

        _authBusinessRules.UserShouldNotBeLockedOut(user, now);

        PasswordVerificationResult verification = _passwordHasher.Verify(request.Password, user.PasswordHash);
        if (verification == PasswordVerificationResult.Failed)
        {
            // Sayaç ve olay, exception'dan ÖNCE kalıcı olur (kaba kuvvete karşı kilitleme).
            user.RegisterFailedAccess(now);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await AuthEvents.FailedAsync(_events, user, now, "invalid_password", cancellationToken);
        }

        _authBusinessRules.CredentialsShouldBeValid(verification != PasswordVerificationResult.Failed, user, now);

        // İterasyon artırıldıysa hash yükseltilir; damga değişmez (kullanıcının diğer oturumları kapanmaz).
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.UpgradePasswordHash(_passwordHasher.Hash(request.Password));

        user.ResetAccessFailed();

        // Şifre doğrulandıktan SONRA: aksi hâlde e-postanın kayıtlı olup olmadığı anlaşılırdı.
        if (!user.EmailConfirmed)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        _authBusinessRules.EmailShouldBeConfirmed(user);

#if (TwoFactor)
        if (user.AuthenticatorType != AuthenticatorType.None)
        {
            string? destination = null;
#if (EmailTwoFactor)
            if (user.AuthenticatorType == AuthenticatorType.Email)
            {
                await _emailVerification.SendCodeAsync(user, VerificationPurpose.SignIn, cancellationToken);
                destination = EmailMask.Mask(user.Email);
            }
#endif

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return LoggedResponse.RequiresTwoFactor(
                new TwoFactorChallenge(user.Id, user.SecurityStamp, user.AuthenticatorType),
                new TwoFactorPrompt(user.AuthenticatorType, destination)
            );
        }
#endif

        AuthResult session = await AuthEvents.SignInAsync(user, request.Client, "password", _tokenIssuer, _sessions, _events, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return LoggedResponse.SignedIn(session);
    }
}
