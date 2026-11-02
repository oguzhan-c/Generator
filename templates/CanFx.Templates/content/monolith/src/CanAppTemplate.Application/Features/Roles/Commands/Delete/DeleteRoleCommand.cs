using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.Roles.Constants;

namespace CanAppTemplate.Application.Features.Roles.Commands.Delete;

public sealed class DeleteRoleCommand : IRequest<DeletedRoleResponse>, ISecuredRequest, ILoggableRequest
{
    public IdentityKey Id { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<string> Permissions => [RolesOperationClaims.Write];
}
