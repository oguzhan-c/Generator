

namespace CanAppTemplate.Application.Services.AuthService;

internal static class EmailMask
{
    /// <summary><c>ornek@gmail.com</c> → <c>or***@gmail.com</c>.</summary>
    public static string Mask(string email)
    {
        int at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0)
            return email;

        int visible = Math.Min(2, at);
        return string.Concat(email.AsSpan(0, visible), new string('*', Math.Max(1, at - visible)), email.AsSpan(at));
    }
}
