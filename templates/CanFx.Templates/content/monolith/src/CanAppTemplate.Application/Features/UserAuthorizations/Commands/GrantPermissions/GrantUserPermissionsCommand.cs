using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.UserAuthorizations.Constants;

namespace CanAppTemplate.Application.Features.UserAuthorizations.Commands.GrantPermissions;

/// <summary>
/// Kullanıcıya rolden bağımsız, doğrudan yetki verir. Aktör yalnızca kendi kapsadığı yetkilerle
/// işlem yapabilir ve kendi yetkilerini değiştiremez. İstisnalar içindir; kalıcı yetki setleri için rol tercih edilmeli.
/// </summary>
public sealed class GrantUserPermissionsCommand : IRequest<GrantedUserPermissionsResponse>, ISecuredRequest, ILoggableRequest
{
    public IdentityKey UserId { get; set; }

    public IReadOnlyCollection<IdentityKey> OperationClaimIds { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<string> Permissions => [UserAuthorizationsOperationClaims.Grant];
}
