

namespace CanAppTemplate.Application.Features.Roles.Queries.GetById;

public sealed record GetByIdRolePermissionDto(IdentityKey Id, string Name, string? Description, bool IsDeprecated);
