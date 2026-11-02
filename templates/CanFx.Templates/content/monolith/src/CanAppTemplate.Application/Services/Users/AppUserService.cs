using CanFx.Domain.Results;
using CanFx.Security.Entities;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Services.Users;

/// <summary>
/// Kullanıcı işlemleri (kayıt, profil, roller). <see cref="AppUser"/> yalnızca alan taşır; kurallar burada.
/// Komutlar (Register, ExternalLogin, UpdateProfile, AssignRole ...) ve seed bunları kullanır.
/// </summary>
public static class AppUserService
{
    /// <summary>Kayıt olan kullanıcı: e-posta doğrulanana kadar giriş yapamaz.</summary>
    public static Result<AppUser> Register(
        string email, string firstName, string lastName, string passwordHash, string? phoneNumber = null, bool marketingConsent = false)
    {
        Result<Success> valid = ValidateProfile(firstName, lastName, phoneNumber);
        if (valid.IsFailure)
            return valid.Errors;

        var user = new AppUser(email);
        Apply(user, firstName, lastName, phoneNumber, marketingConsent);
        user.SetPasswordHash(passwordHash);
        return user;
    }

    /// <summary>Yönetici ya da seed tarafından oluşturulan (e-postası onaylı) kullanıcı.</summary>
    public static Result<AppUser> Create(string email, string firstName, string lastName, string passwordHash) =>
        Register(email, firstName, lastName, passwordHash).Tap(user => user.ConfirmEmail());

    /// <summary>Dış sağlayıcıyla (Google, GitHub ...) ilk girişte oluşturulan kullanıcı: şifresi yoktur, e-postası onaylıdır.</summary>
    public static Result<AppUser> RegisterExternal(string email, string firstName, string lastName)
    {
        Result<Success> valid = ValidateProfile(firstName, lastName, null);
        if (valid.IsFailure)
            return valid.Errors;

        var user = new AppUser(email);
        Apply(user, firstName, lastName, null, marketingConsent: false);
        user.ConfirmEmail();
        return user;
    }

    public static Result<Success> UpdateProfile(this AppUser user, string firstName, string lastName, string? phoneNumber, bool marketingConsent)
    {
        ArgumentNullException.ThrowIfNull(user);
        Result<Success> valid = ValidateProfile(firstName, lastName, phoneNumber);
        if (valid.IsFailure)
            return valid;

        Apply(user, firstName, lastName, phoneNumber, marketingConsent);
        return Result.Success;
    }

    public static string FullName(this AppUser user) => $"{user.FirstName} {user.LastName}".Trim();

    public static void AddRole(this AppUser user, Role<IdentityKey> role)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(role);
        if (user.UserRoles.All(r => r.RoleId != role.Id))
            user.UserRoles.Add(new UserRole<IdentityKey>(user.Id, role.Id));
    }

    public static void RemoveRole(this AppUser user, IdentityKey roleId)
    {
        ArgumentNullException.ThrowIfNull(user);
        UserRole<IdentityKey>? userRole = user.UserRoles.FirstOrDefault(r => r.RoleId == roleId);
        if (userRole is not null)
            user.UserRoles.Remove(userRole);
    }

    private static void Apply(AppUser user, string firstName, string lastName, string? phoneNumber, bool marketingConsent)
    {
        user.FirstName = firstName.Trim();
        user.LastName = lastName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        user.MarketingConsent = marketingConsent;
    }

    private static Result<Success> ValidateProfile(string firstName, string lastName, string? phoneNumber) =>
        Result.Validate(
            Required(firstName, nameof(AppUser.FirstName), AppUser.NameMaxLength),
            Required(lastName, nameof(AppUser.LastName), AppUser.NameMaxLength),
            MaxLength(phoneNumber, nameof(AppUser.PhoneNumber), AppUser.PhoneMaxLength));

    private static Error? Required(string? value, string field, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? Error.Validation("required", $"{field} boş olamaz.", field) : MaxLength(value, field, maxLength);

    private static Error? MaxLength(string? value, string field, int maxLength) =>
        value is not null && value.Trim().Length > maxLength
            ? Error.Validation("max_length", $"{field} en fazla {maxLength} karakter olabilir.", field)
            : null;
}
