using CanFx.Application;
using CanFx.Application.Authorization;
using CanFx.Mediator;
using CanFx.Security.Entities;
using CanAppTemplate.Application.Features.Roles.Constants;

namespace CanAppTemplate.Application.Features.Roles.Commands.RevokePermissions;

/// <summary>Role yetki geri alır. Aktör yalnızca kendi kapsadığı yetkilerle işlem yapabilir (<see cref="IAccessGrantGuard"/>).</summary>
public sealed class RevokeRolePermissionsCommand : IRequest<RevokedRolePermissionsResponse>, ISecuredRequest, ILoggableRequest
{
    public IdentityKey RoleId { get; set; }

    public IReadOnlyCollection<IdentityKey> OperationClaimIds { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<string> Permissions => [RolesOperationClaims.Grant];
}
