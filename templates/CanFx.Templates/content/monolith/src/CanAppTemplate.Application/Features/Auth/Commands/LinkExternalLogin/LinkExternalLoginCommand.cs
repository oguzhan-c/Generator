using CanFx.Application;
using CanFx.Mediator;

namespace CanAppTemplate.Application.Features.Auth.Commands.LinkExternalLogin;

/// <summary>
/// Giriş yapmış kullanıcının hesabına dış hesap bağlar. Kullanıcı, sağlayıcıya giden şifreli state'ten gelir (dönüşte
/// oturum cookie'si gönderilmez); istemciden alınmaz.
/// </summary>
public sealed record LinkExternalLoginCommand(IdentityKey UserId, string Provider, string ProviderDisplayName, string ProviderKey)
    : IRequest, ITransactionalRequest, ILoggableRequest;
