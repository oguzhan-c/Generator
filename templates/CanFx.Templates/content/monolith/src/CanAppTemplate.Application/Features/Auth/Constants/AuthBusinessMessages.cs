namespace CanAppTemplate.Application.Features.Auth.Constants;

/// <summary>
/// Kimlik doğrulama hata kodları: ProblemDetails <c>code</c> alanı (istemciyle sözleşme) ve çeviri anahtarı. Metinler kodda
/// değil, <c>Features/Auth/Localization/auth.{dil}.json</c>'da; yer tutucular (<c>{max}</c>) <c>exception.With("max", değer)</c> ile doldurulur.
/// </summary>
public static class AuthBusinessMessages
{
    public const string Unauthorized = "auth.unauthorized";

    public const string InvalidCredentials = "auth.invalid_credentials";

    public const string LockedOut = "auth.locked_out";

    public const string EmailNotConfirmed = "auth.email_not_confirmed";

    public const string EmailAlreadyConfirmed = "auth.email_already_confirmed";

    public const string EmailTaken = "auth.email_taken";

    public const string InvalidVerificationCode = "auth.invalid_verification_code";

    public const string InvalidTwoFactorCode = "auth.invalid_two_factor_code";

    public const string TwoFactorExpired = "auth.two_factor_expired";

    public const string TwoFactorNotEmail = "auth.two_factor_not_email";

    public const string PasskeyFailed = "auth.passkey_failed";

    public const string PasskeyExpired = "auth.passkey_expired";

    public const string PasskeyInvalidResponse = "auth.passkey_invalid_response";

    public const string SessionExpired = "auth.session_expired";

    public const string InvalidPassword = "auth.invalid_password";

    public const string ExternalEmailNotVerified = "auth.external_email_not_verified";

    public const string ExternalAlreadyLinked = "auth.external_already_linked";

    public const string ExternalProviderLinked = "auth.external_provider_linked";
}
