namespace CanAppTemplate.Application.Features.Account.Constants;

/// <summary>
/// Hesap güvenliği hata kodları: ProblemDetails <c>code</c> alanı (istemciyle sözleşme) ve çeviri anahtarı. Metinler kodda
/// değil, <c>Features/Account/Localization/account.{dil}.json</c>'da; yer tutucular (<c>{max}</c>) <c>exception.With("max", değer)</c> ile doldurulur.
/// </summary>
public static class AccountBusinessMessages
{
    public const string CurrentPasswordInvalid = "account.current_password_invalid";

    public const string OtpAlreadyEnabled = "account.otp_already_enabled";

    public const string OtpCodeInvalid = "account.otp_code_invalid";

    public const string EmailNotConfirmed = "account.email_not_confirmed";

    public const string TwoFactorDisabled = "account.two_factor_disabled";

    public const string PasskeyNotFound = "account.passkey_not_found";

    public const string PasskeyWrongUser = "account.passkey_wrong_user";

    public const string PasskeyLimit = "account.passkey_limit";

    public const string PasskeyRegistrationFailed = "account.passkey_registration_failed";

    public const string PasskeyRegistrationExpired = "account.passkey_registration_expired";

    public const string ExternalLoginNotFound = "account.external_login_not_found";

    public const string LastSignInMethod = "account.last_sign_in_method";
}
