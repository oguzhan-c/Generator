using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.Auth.Commands.Login;
using CanAppTemplate.Application.Services.AuthService;
using CanAppTemplate.Application.Services.Users;

namespace CanAppTemplate.Application.Features.Auth.Commands.ExternalLogin;

/// <summary>
/// Dış sağlayıcıyla (Google, Microsoft, GitHub) giriş. Kullanıcı şu sırayla bulunur:
/// <list type="number">
/// <item>Bu sağlayıcı hesabı bir kullanıcıya bağlıysa o kullanıcı.</item>
/// <item>Sağlayıcı e-postayı DOĞRULANMIŞ verdiyse aynı e-postalı kullanıcı (hesap otomatik bağlanır).</item>
/// <item>Hiçbiri yoksa ve e-posta doğrulanmışsa yeni müşteri hesabı (şifresiz).</item>
/// </list>
/// Doğrulanmamış e-postayla mevcut hesaba bağlanmaz: aksi hâlde başkasının e-postasını sağlayıcıya yazan biri o
/// hesabı ele geçirebilirdi. İki adımlı doğrulama açıksa şifreli girişteki gibi ikinci adım istenir.
/// </summary>
public sealed record ExternalLoginCommand(
    string Provider,
    string ProviderDisplayName,
    string ProviderKey,
    string? Email,
    bool EmailVerified,
    string? FirstName,
    string? LastName,
    string? FullName,
    ClientInfo Client) : IRequest<LoggedResponse>, ITransactionalRequest, ILoggableRequest;
