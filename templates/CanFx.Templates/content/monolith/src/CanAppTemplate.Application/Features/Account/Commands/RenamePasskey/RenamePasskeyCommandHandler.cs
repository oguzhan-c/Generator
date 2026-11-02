using CanFx.Mediator;
using CanFx.Persistence.Repositories;
using CanFx.Security.Entities;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;
using CanAppTemplate.Application.Features.Account.Rules;
using CanAppTemplate.Application.Features.Auth.Rules;

namespace CanAppTemplate.Application.Features.Account.Commands.RenamePasskey;

public sealed class RenamePasskeyCommandHandler : IRequestHandler<RenamePasskeyCommand>
{
    private readonly IRepository<UserPasskey<IdentityKey>, IdentityKey> _passkeys;
    private readonly AuthBusinessRules _authBusinessRules;
    private readonly AccountBusinessRules _accountBusinessRules;

    public RenamePasskeyCommandHandler(IRepository<UserPasskey<IdentityKey>, IdentityKey> passkeys, AuthBusinessRules authBusinessRules, AccountBusinessRules accountBusinessRules)
    {
        _passkeys = passkeys;
        _authBusinessRules = authBusinessRules;
        _accountBusinessRules = accountBusinessRules;
    }

    public async Task Handle(RenamePasskeyCommand request, CancellationToken cancellationToken)
    {
        IdentityKey userId = _authBusinessRules.CurrentUserIdShouldBeKnown();
        UserPasskey<IdentityKey>? passkey = await _passkeys.GetAsync(p => p.Id == request.Id && p.UserId == userId, cancellationToken: cancellationToken);
        _accountBusinessRules.PasskeyShouldExist(passkey is not null);

        passkey.Rename(request.Name);
    }
}
