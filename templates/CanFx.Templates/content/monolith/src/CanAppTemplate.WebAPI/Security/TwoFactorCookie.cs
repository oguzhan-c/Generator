using System.Security.Cryptography;
using System.Text.Json;
using CanFx.WebApi.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.WebAPI.Security;

/// <summary>
/// Şifre doğrulandıktan sonra ikinci adım beklenirken "yarım giriş" bilgisi. İstemciye ŞİFRELİ ve imzalı (ASP.NET
/// Data Protection), 10 dakika geçerli, HttpOnly bir cookie olarak verilir: JavaScript okuyamaz, istemci içeriğini
/// değiştiremez, header'dan okunmaz.
/// </summary>
public sealed class TwoFactorCookie
{
    private const string CookieName = "pz_2fa";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector _protector;
    private readonly AuthCookieOptions _cookieOptions;

    public TwoFactorCookie(IDataProtectionProvider dataProtection, AuthCookieOptions cookieOptions)
    {
        _protector = dataProtection.CreateProtector("CanAppTemplate.TwoFactorLogin").ToTimeLimitedDataProtector();
        _cookieOptions = cookieOptions;
    }

    public void Write(HttpResponse response, TwoFactorChallenge challenge)
    {
        string payload = _protector.Protect(JsonSerializer.Serialize(challenge), Lifetime);
        response.Cookies.Append(CookieName, payload, CookieOptions(Lifetime));
    }

    /// <summary>Cookie yoksa, süresi dolmuşsa ya da kurcalanmışsa <see langword="null"/>.</summary>
    public TwoFactorChallenge? Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out string? payload) || string.IsNullOrEmpty(payload))
            return null;

        try
        {
            return JsonSerializer.Deserialize<TwoFactorChallenge>(_protector.Unprotect(payload));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return null;
        }
    }

    public void Clear(HttpResponse response) => response.Cookies.Delete(CookieName, CookieOptions(null));

    private CookieOptions CookieOptions(TimeSpan? maxAge) =>
        new()
        {
            HttpOnly = true,
            Secure = _cookieOptions.Secure,
            SameSite = _cookieOptions.SameSite,
            Path = "/api/auth",
            MaxAge = maxAge,
            IsEssential = true,
        };
}
