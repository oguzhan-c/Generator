using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.UserAuthorizations.Constants;

namespace CanAppTemplate.Application.Features.UserAuthorizations.Commands.RemoveRole;

/// <summary>
/// Kullanıcıya rol kaldırır. Aktör rolün TÜM yetkilerini kapsamalı (Admin rolünü yalnızca Admin
/// alabilir) ve kendi rollerini değiştiremez. Son yönetici kaldırılamaz. Değişiklik
/// token süresini beklemeden sonraki istekte geçerli olur.
/// </summary>
public sealed class RemoveUserRoleCommand : IRequest<RemovedUserRoleResponse>, ISecuredRequest, ILoggableRequest
{
    public IdentityKey UserId { get; set; }

    public IdentityKey RoleId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<string> Permissions => [UserAuthorizationsOperationClaims.AssignRole];
}
