namespace CanAppTemplate.Application.Features.UserAuthorizations.Constants;

/// <summary>
/// Kullanıcı yetkileri hata kodları: ProblemDetails <c>code</c> alanı (istemciyle sözleşme) ve çeviri anahtarı. Metinler kodda
/// değil, <c>Features/UserAuthorizations/Localization/user_authorizations.{dil}.json</c>'da; yer tutucular (<c>{max}</c>) <c>exception.With("max", değer)</c> ile doldurulur.
/// </summary>
public static class UserAuthorizationsBusinessMessages
{
    public const string UserNotExists = "user_authorizations.user_not_exists";
}
