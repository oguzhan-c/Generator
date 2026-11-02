using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.Roles.Constants;

namespace CanAppTemplate.Application.Features.Roles.Queries.GetById;

public sealed class GetByIdRoleQuery : IRequest<GetByIdRoleResponse>, ISecuredRequest
{
    public IdentityKey Id { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<string> Permissions => [RolesOperationClaims.Read];
}
