using CanFx.Mediator;
#if (TwoFactor || Passkey || ExternalLogin)
using CanFx.Persistence.Repositories;
using CanFx.Security.Entities;
using Microsoft.EntityFrameworkCore;
#endif
#if (Passkey)
using CanFx.Security.Passkeys;
using Microsoft.Extensions.DependencyInjection;
#endif
using CanAppTemplate.Application.Features.Auth.Rules;
using CanAppTemplate.Application.Services.AuthService;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Features.Account.Queries.GetAccountSecurity;

public sealed class GetAccountSecurityQueryHandler : IRequestHandler<GetAccountSecurityQuery, AccountSecurityDto>
{
#if (Passkey)
    private readonly IRepository<UserPasskey<IdentityKey>, IdentityKey> _passkeys;
    private readonly IServiceProvider _services;
#endif
#if (ExternalLogin)
    private readonly IRepository<UserLogin<IdentityKey>, IdentityKey> _logins;
#endif
#if (TwoFactor)
    private readonly IRepository<RecoveryCode<IdentityKey>, IdentityKey> _recoveryCodes;
#endif
    private readonly AuthBusinessRules _authBusinessRules;

    public GetAccountSecurityQueryHandler(
#if (Passkey)
        IRepository<UserPasskey<IdentityKey>, IdentityKey> passkeys,
        IServiceProvider services,
#endif
#if (ExternalLogin)
        IRepository<UserLogin<IdentityKey>, IdentityKey> logins,
#endif
#if (TwoFactor)
        IRepository<RecoveryCode<IdentityKey>, IdentityKey> recoveryCodes,
#endif
        AuthBusinessRules authBusinessRules)
    {
#if (Passkey)
        _passkeys = passkeys;
        _services = services;
#endif
#if (ExternalLogin)
        _logins = logins;
#endif
#if (TwoFactor)
        _recoveryCodes = recoveryCodes;
#endif
        _authBusinessRules = authBusinessRules;
    }

    public async Task<AccountSecurityDto> Handle(GetAccountSecurityQuery request, CancellationToken cancellationToken)
    {
        AppUser user = await _authBusinessRules.CurrentUserShouldExist(enableTracking: false, cancellationToken: cancellationToken);
#if (Passkey)
        List<UserPasskey<IdentityKey>> passkeys = await _passkeys.Query(enableTracking: false).Where(p => p.UserId == user.Id).OrderBy(p => p.CreatedAt).ToListAsync(cancellationToken);
#endif
#if (ExternalLogin)
        List<UserLogin<IdentityKey>> logins = await _logins.Query(enableTracking: false).Where(l => l.UserId == user.Id).OrderBy(l => l.LoginProvider).ToListAsync(cancellationToken);
#endif
#if (TwoFactor)
        int recoveryLeft = await _recoveryCodes.CountAsync(c => c.UserId == user.Id && c.UsedAt == null, cancellationToken: cancellationToken);
#endif

        return new AccountSecurityDto(
            user.Email,
            user.EmailConfirmed,
            user.PasswordHash is not null
#if (TwoFactor)
            , user.AuthenticatorType
            , recoveryLeft
#endif
#if (Passkey)
            , _services.GetService<IPasskeyService>() is not null
            , passkeys.Select(PasskeyDto.From).ToList()
#endif
#if (ExternalLogin)
            , logins.Select(ExternalLoginDto.From).ToList()
#endif
        );
    }
}
