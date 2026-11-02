using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.UserAuthorizations.Constants;

namespace CanAppTemplate.Application.Features.UserAuthorizations.Commands.AssignRole;

/// <summary>
/// Kullanıcıya rol atar. Aktör rolün TÜM yetkilerini kapsamalı (Admin rolünü yalnızca Admin
/// verebilir) ve kendi rollerini değiştiremez. Değişiklik
/// token süresini beklemeden sonraki istekte geçerli olur.
/// </summary>
public sealed class AssignUserRoleCommand : IRequest<AssignedUserRoleResponse>, ISecuredRequest, ILoggableRequest
{
    public IdentityKey UserId { get; set; }

    public IdentityKey RoleId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<string> Permissions => [UserAuthorizationsOperationClaims.AssignRole];
}
