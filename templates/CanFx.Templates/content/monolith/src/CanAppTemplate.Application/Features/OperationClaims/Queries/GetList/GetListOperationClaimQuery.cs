using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.OperationClaims.Constants;

namespace CanAppTemplate.Application.Features.OperationClaims.Queries.GetList;

/// <summary>
/// Yetki kataloğu. Yetkiler KODLA tanımlanır (<c>AppOperationClaims</c>) ve açılışta veritabanına eşitlenir; yönetim
/// ekranı yetki oluşturmaz / silmez, yalnızca rollere ve kullanıcılara dağıtır.
/// </summary>
public sealed class GetListOperationClaimQuery : IRequest<IReadOnlyList<GetListOperationClaimListItemDto>>, ISecuredRequest
{
    /// <summary>Koddan kaldırılmış (kullanımdan kalkmış) yetkiler de listelensin mi.</summary>
    public bool IncludeDeprecated { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyCollection<string> Permissions => [OperationClaimsOperationClaims.Read];
}
