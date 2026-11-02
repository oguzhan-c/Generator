using CanFx.Application;
using CanFx.Application.Requests;
using CanFx.Application.Responses;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.Roles.Constants;

namespace CanAppTemplate.Application.Features.Roles.Queries.GetList;

public sealed class GetListRoleQuery : IRequest<GetListResponse<GetListRoleListItemDto>>, ISecuredRequest
{
    public PageRequest PageRequest { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<string> Permissions => [RolesOperationClaims.Read];
}
