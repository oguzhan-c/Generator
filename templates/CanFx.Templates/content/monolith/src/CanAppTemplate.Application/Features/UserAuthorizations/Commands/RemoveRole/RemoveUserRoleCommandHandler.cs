using CanFx.Application.Authorization;
using CanFx.Mediator;
using CanFx.Persistence.Repositories;
using CanFx.Security.Authorization;
using CanFx.Security.Entities;
using CanAppTemplate.Application.Features.Roles.Rules;
using CanAppTemplate.Application.Features.UserAuthorizations.Rules;
using CanAppTemplate.Domain.Entities;
using CanAppTemplate.Application.Services.Users;

namespace CanAppTemplate.Application.Features.UserAuthorizations.Commands.RemoveRole;

public sealed class RemoveUserRoleCommandHandler : IRequestHandler<RemoveUserRoleCommand, RemovedUserRoleResponse>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly RoleBusinessRules _roleBusinessRules;
    private readonly UserAuthorizationBusinessRules _userAuthorizationBusinessRules;
    private readonly IAccessGrantGuard _accessGrantGuard;
    private readonly IUserAuthorizationStore _authorizationStore;

    public RemoveUserRoleCommandHandler(
        IUnitOfWork unitOfWork,
        RoleBusinessRules roleBusinessRules,
        UserAuthorizationBusinessRules userAuthorizationBusinessRules,
        IAccessGrantGuard accessGrantGuard,
        IUserAuthorizationStore authorizationStore)
    {
        _unitOfWork = unitOfWork;
        _roleBusinessRules = roleBusinessRules;
        _userAuthorizationBusinessRules = userAuthorizationBusinessRules;
        _accessGrantGuard = accessGrantGuard;
        _authorizationStore = authorizationStore;
    }

    public async Task<RemovedUserRoleResponse> Handle(RemoveUserRoleCommand request, CancellationToken cancellationToken)
    {
        AppUser user = await _userAuthorizationBusinessRules.UserIdShouldExistWhenSelected(request.UserId, cancellationToken);
        Role<IdentityKey> role = await _roleBusinessRules.RoleIdShouldExistWhenSelected(request.RoleId, cancellationToken);
        _accessGrantGuard.EnsureCanAssignRole(user.Id.ToString(), role.Name, RoleBusinessRules.PermissionNamesOf(role));

        if (_accessGrantGuard.IsAdminRole(role.Name) && user.UserRoles.Any(ur => ur.RoleId == role.Id))
            _accessGrantGuard.EnsureAdminRemains(await _userAuthorizationBusinessRules.CountUsersInRole(role.Id, cancellationToken) - 1);

        user.RemoveRole(role.Id);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _authorizationStore.Invalidate(user.Id.ToString());

        return new RemovedUserRoleResponse(user.Id, role.Id, role.Name);
    }
}
