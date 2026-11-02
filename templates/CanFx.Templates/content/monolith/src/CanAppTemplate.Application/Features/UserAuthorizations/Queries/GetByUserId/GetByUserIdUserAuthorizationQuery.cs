using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.UserAuthorizations.Constants;

namespace CanAppTemplate.Application.Features.UserAuthorizations.Queries.GetByUserId;

/// <summary>Kullanıcının rolleri, doğrudan yetkileri ve etkin yetkileri.</summary>
public sealed class GetByUserIdUserAuthorizationQuery : IRequest<GetByUserIdUserAuthorizationResponse>, ISecuredRequest
{
    public IdentityKey UserId { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<string> Permissions => [UserAuthorizationsOperationClaims.Read];
}
