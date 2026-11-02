using CanFx.Mediator;
using CanFx.Persistence.Repositories;
using CanFx.Security.Entities;
using Microsoft.EntityFrameworkCore;

namespace CanAppTemplate.Application.Features.OperationClaims.Queries.GetList;

public sealed class GetListOperationClaimQueryHandler : IRequestHandler<GetListOperationClaimQuery, IReadOnlyList<GetListOperationClaimListItemDto>>
{
    private readonly IRepository<OperationClaim<IdentityKey>, IdentityKey> _operationClaimRepository;

    public GetListOperationClaimQueryHandler(IRepository<OperationClaim<IdentityKey>, IdentityKey> operationClaimRepository) =>
        _operationClaimRepository = operationClaimRepository;

    public async Task<IReadOnlyList<GetListOperationClaimListItemDto>> Handle(GetListOperationClaimQuery request, CancellationToken cancellationToken)
    {
        IQueryable<OperationClaim<IdentityKey>> query = _operationClaimRepository.Query(enableTracking: false);
        if (!request.IncludeDeprecated)
            query = query.Where(c => !c.IsDeprecated);

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new GetListOperationClaimListItemDto(c.Id, c.Name, c.Description, c.IsDeprecated))
            .ToListAsync(cancellationToken);
    }
}
