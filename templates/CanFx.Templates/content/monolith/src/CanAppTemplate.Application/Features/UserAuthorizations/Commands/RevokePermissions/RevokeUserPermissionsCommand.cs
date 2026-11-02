using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.UserAuthorizations.Constants;

namespace CanAppTemplate.Application.Features.UserAuthorizations.Commands.RevokePermissions;

/// <summary>
/// Kullanıcıya rolden bağımsız, doğrudan yetki geri alır. Aktör yalnızca kendi kapsadığı yetkilerle
/// işlem yapabilir ve kendi yetkilerini değiştiremez. İstisnalar içindir; kalıcı yetki setleri için rol tercih edilmeli.
/// </summary>
public sealed class RevokeUserPermissionsCommand : IRequest<RevokedUserPermissionsResponse>, ISecuredRequest, ILoggableRequest
{
    public IdentityKey UserId { get; set; }

    public IReadOnlyCollection<IdentityKey> OperationClaimIds { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<string> Permissions => [UserAuthorizationsOperationClaims.Grant];
}
