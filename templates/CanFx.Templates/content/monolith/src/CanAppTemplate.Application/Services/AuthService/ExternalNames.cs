using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Services.AuthService;

internal static class ExternalNames
{
    /// <summary>Sağlayıcının verdiği adlardan ad/soyad; yoksa tam addan ya da e-postanın kullanıcı adı kısmından.</summary>
    public static (string FirstName, string LastName) Split(string? givenName, string? familyName, string? fullName, string email)
    {
        string first = givenName?.Trim() ?? string.Empty;
        string last = familyName?.Trim() ?? string.Empty;

        if (first.Length == 0 && !string.IsNullOrWhiteSpace(fullName))
        {
            string[] parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            first = parts.Length > 1 ? string.Join(' ', parts[..^1]) : parts[0];
            if (last.Length == 0 && parts.Length > 1)
                last = parts[^1];
        }

        if (first.Length == 0)
            first = email[..Math.Max(1, email.IndexOf('@', StringComparison.Ordinal))];

        if (last.Length == 0)
            last = "-";

        return (Truncate(first), Truncate(last));
    }

    private static string Truncate(string value) => value.Length <= AppUser.NameMaxLength ? value : value[..AppUser.NameMaxLength];
}
