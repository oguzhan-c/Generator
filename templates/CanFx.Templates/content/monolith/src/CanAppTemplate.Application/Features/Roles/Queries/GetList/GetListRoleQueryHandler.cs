using CanFx.Application.Requests;
using CanFx.Application.Responses;
using CanFx.Mediator;
using CanFx.Persistence.Paging;
using CanFx.Persistence.Repositories;
using CanFx.Security.Entities;
using CanAppTemplate.Application.Features.Roles.Rules;

namespace CanAppTemplate.Application.Features.Roles.Queries.GetList;

public sealed class GetListRoleQueryHandler : IRequestHandler<GetListRoleQuery, GetListResponse<GetListRoleListItemDto>>
{
    private readonly IRepository<Role<IdentityKey>, IdentityKey> _roleRepository;

    public GetListRoleQueryHandler(IRepository<Role<IdentityKey>, IdentityKey> roleRepository) => _roleRepository = roleRepository;

    public async Task<GetListResponse<GetListRoleListItemDto>> Handle(GetListRoleQuery request, CancellationToken cancellationToken)
    {
        IPaginate<Role<IdentityKey>> roles = await _roleRepository.GetListAsync(
            orderBy: q => q.OrderBy(r => r.Name),
            include: RoleBusinessRules.IncludeOperationClaims,
            index: request.PageRequest.Page,
            size: request.PageRequest.PageSize,
            enableTracking: false,
            cancellationToken: cancellationToken);

        return roles.ToGetListResponse(r => new GetListRoleListItemDto(r.Id, r.Name, r.Description, r.IsSystem, RoleBusinessRules.PermissionNamesOf(r).Count));
    }
}
