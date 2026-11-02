using CanFx.Application;
using CanFx.Mediator;
using CanFx.Persistence.Repositories;
using CanFx.Security.Entities;
using CanFx.Security.Events;
using CanFx.Security.Sessions;
using Microsoft.EntityFrameworkCore;
using CanAppTemplate.Application.Features.Auth.Commands.Login;
using CanAppTemplate.Application.Features.Auth.Rules;
using CanAppTemplate.Application.Services.AuthService;
using CanAppTemplate.Domain.Entities;
using CanAppTemplate.Domain.Identity;
using CanAppTemplate.Application.Services.Users;

namespace CanAppTemplate.Application.Features.Auth.Commands.ExternalLogin;

public sealed class ExternalLoginCommandHandler : IRequestHandler<ExternalLoginCommand, LoggedResponse>
{
    private readonly IRepository<AppUser, IdentityKey> _users;
    private readonly IRepository<Role<IdentityKey>, IdentityKey> _roles;
    private readonly AuthTokenIssuer _tokenIssuer;
    private readonly EmailVerification _emailVerification;
    private readonly IUserSessionService _sessions;
    private readonly ISecurityEvents _events;
    private readonly TimeProvider _timeProvider;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuthBusinessRules _authBusinessRules;

    public ExternalLoginCommandHandler(
        IRepository<AppUser, IdentityKey> users,
        IRepository<Role<IdentityKey>, IdentityKey> roles,
        AuthTokenIssuer tokenIssuer,
        EmailVerification emailVerification,
        IUserSessionService sessions,
        ISecurityEvents events,
        TimeProvider timeProvider,
        IUnitOfWork unitOfWork,
        AuthBusinessRules authBusinessRules)
    {
        _users = users;
        _roles = roles;
        _tokenIssuer = tokenIssuer;
        _emailVerification = emailVerification;
        _sessions = sessions;
        _events = events;
        _timeProvider = timeProvider;
        _unitOfWork = unitOfWork;
        _authBusinessRules = authBusinessRules;
    }

    public async Task<LoggedResponse> Handle(ExternalLoginCommand request, CancellationToken cancellationToken)
    {
        string provider = request.Provider.Trim().ToLowerInvariant();
        string key = request.ProviderKey.Trim();
        DateTimeOffset now = _timeProvider.GetUtcNow();

        AppUser? user = await _users.GetAsync(
            u => u.Logins.Any(l => l.LoginProvider == provider && l.ProviderKey == key),
            include: q => q.WithRolesAndPermissions().Include(u => u.Logins),
            cancellationToken: cancellationToken
        );

        if (user is null)
        {
            _authBusinessRules.ExternalEmailShouldBeVerified(request.EmailVerified && !string.IsNullOrWhiteSpace(request.Email));

            string normalizedEmail = request.Email!.Trim().ToUpperInvariant();
            user = await _users.GetAsync(
                u => u.NormalizedEmail == normalizedEmail,
                include: q => q.WithRolesAndPermissions().Include(u => u.Logins),
                cancellationToken: cancellationToken
            );

            if (user is null)
            {
                // Silinmiş bir hesabın e-postası: yeni hesap açılamaz (benzersiz e-posta).
                await _authBusinessRules.EmailShouldNotBeTaken(normalizedEmail, cancellationToken);
                user = await CreateCustomerAsync(request, cancellationToken);
            }

            // Aynı sağlayıcıda başka bir hesap zaten bağlıysa otomatik bağlama yapılmaz.
            _authBusinessRules.ExternalLoginShouldBeAdded(user.AddLogin(provider, key, request.ProviderDisplayName, now));

            await _events.PublishAsync(
                new SecurityEvent(SecurityEventTypes.ExternalLoginLinked, user.Id.ToString()) { Properties = new Dictionary<string, string?> { ["provider"] = provider } },
                cancellationToken
            );
        }

        _authBusinessRules.UserShouldNotBeLockedOut(user, now);

        // Sağlayıcı e-postayı doğruladıysa hesabın e-postası da doğrulanmış sayılır (kod beklemeye gerek yok).
        if (!user.EmailConfirmed)
        {
            _authBusinessRules.EmailShouldBeVerifiedByProvider(
                request.EmailVerified && string.Equals(user.NormalizedEmail, request.Email?.Trim().ToUpperInvariant(), StringComparison.Ordinal));
            user.ConfirmEmail();
        }

        if (user.AuthenticatorType != AuthenticatorType.None)
        {
            string? destination = null;
            if (user.AuthenticatorType == AuthenticatorType.Email)
            {
                await _emailVerification.SendCodeAsync(user, VerificationPurpose.SignIn, cancellationToken);
                destination = EmailMask.Mask(user.Email);
            }

            return LoggedResponse.RequiresTwoFactor(
                new TwoFactorChallenge(user.Id, user.SecurityStamp, user.AuthenticatorType),
                new TwoFactorPrompt(user.AuthenticatorType, destination)
            );
        }

        AuthResult session = await AuthEvents.SignInAsync(user, request.Client, provider, _tokenIssuer, _sessions, _events, cancellationToken);
        return LoggedResponse.SignedIn(session);
    }

    private async Task<AppUser> CreateCustomerAsync(ExternalLoginCommand request, CancellationToken cancellationToken)
    {
        (string firstName, string lastName) = ExternalNames.Split(request.FirstName, request.LastName, request.FullName, request.Email!);

        AppUser user = AppUserService.RegisterExternal(request.Email!, firstName, lastName).OrThrow();
        user.AddRole(await DefaultRoles.GetAsync(_roles, cancellationToken));
        await _users.AddAsync(user, cancellationToken);
        if (IdentityKeys.IsDatabaseGenerated)
            await _unitOfWork.SaveChangesAsync(cancellationToken); // int / long anahtar: Id'ye bağlı kayıtlardan (doğrulama kodu, oturum) önce
        return user;
    }
}
