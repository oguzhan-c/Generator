using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.Roles.Constants;

namespace CanAppTemplate.Application.Features.Roles.Commands.Update;

public sealed class UpdateRoleCommand : IRequest<UpdatedRoleResponse>, ISecuredRequest, ILoggableRequest
{
    public IdentityKey Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<string> Permissions => [RolesOperationClaims.Write];
}
