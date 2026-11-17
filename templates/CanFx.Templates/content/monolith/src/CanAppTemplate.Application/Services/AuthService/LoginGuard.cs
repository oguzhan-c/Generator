using CanFx.Security.Lockout;
#if (Settings)
using CanFx.Settings;
#endif

namespace CanAppTemplate.Application.Services.AuthService;

/// <summary>
/// Hatalı girişlere karşı iki sınır. (1) Hesap + IP: aynı IP'den art arda hatalı deneme yalnızca O IP'yi engeller;
/// saldırgan başkasının hesabını kilitleyemez, hesabın sahibi başka IP'den girer. (2) Hesap geneli: tüm IP'lerden toplam
/// hatalı deneme sınırı (çok IP'li saldırıya karşı); ulaşılınca hesap herkes için kilitlenir (<c>AppUser.LockoutEnd</c>).
/// </summary>
/// <remarks>
#if (Settings)
/// Sınırlar yönetim ekranından değişir (<c>/api/settings</c> → "Giriş güvenliği"); yalnızca uygulama genelinde.
#else
/// Sınırlar <c>AddCanSecurity(o =&gt; o.LoginThrottle ...)</c> ile verilir (varsayılan: IP başına 5, hesap başına 20, 15 dk).
#endif
/// IP <c>HttpContext.Connection.RemoteIpAddress</c>'tir: uygulama bir proxy / yük dengeleyici arkasındaysa
/// <c>UseForwardedHeaders</c> doğru yapılandırılmalı, yoksa herkes proxy'nin IP'sini paylaşır.
/// </remarks>
public sealed class LoginGuard
{
    private readonly ILoginThrottle _throttle;
    private readonly LoginThrottleOptions _options;
#if (Settings)
    private readonly ISettings _settings;

    public LoginGuard(ILoginThrottle throttle, LoginThrottleOptions options, ISettings settings)
    {
        _throttle = throttle;
        _options = options;
        _settings = settings;
    }
#else

    public LoginGuard(ILoginThrottle throttle, LoginThrottleOptions options)
    {
        _throttle = throttle;
        _options = options;
    }
#endif

    /// <summary>Bu IP, bu e-posta için şu an engelli mi (kayıtlı olmayan e-postalarda da sorulur).</summary>
    public Task<bool> IsBlockedAsync(string email, ClientInfo client, CancellationToken cancellationToken) =>
        _throttle.IsBlockedAsync(email, client.IpAddress, cancellationToken);

    /// <summary>
    /// Hatalı denemeyi IP için kaydeder ve hesap genelindeki sınırı döner:
    /// <c>user.RegisterFailedAccess(now, accountMaxAttempts, lockoutDuration)</c>.
    /// </summary>
    public async Task<(int AccountMaxAttempts, TimeSpan LockoutDuration)> RegisterFailureAsync(string email, ClientInfo client, CancellationToken cancellationToken)
    {
        (LoginThrottleLimits limits, int accountMaxAttempts) = await LimitsAsync(cancellationToken);
        await _throttle.RegisterFailureAsync(email, client.IpAddress, limits, cancellationToken);
        return (accountMaxAttempts, limits.LockoutDuration);
    }

    /// <summary>Başarılı girişte IP sayacını siler.</summary>
    public Task ResetAsync(string email, ClientInfo client, CancellationToken cancellationToken) =>
        _throttle.ResetAsync(email, client.IpAddress, cancellationToken);

#if (Settings)
    private async Task<(LoginThrottleLimits Limits, int AccountMaxAttempts)> LimitsAsync(CancellationToken cancellationToken)
    {
        TimeSpan lockout = TimeSpan.FromMinutes(await _settings.GetAsync(CanSettingKeys.Login.LockoutMinutes, cancellationToken));
        int perIp = await _settings.GetAsync(CanSettingKeys.Login.MaxAttemptsPerIp, cancellationToken);
        int perAccount = await _settings.GetAsync(CanSettingKeys.Login.AccountMaxAttempts, cancellationToken);
        _ = _options; // varsayılanlar ayarların varsayılan değerleridir
        return (new LoginThrottleLimits(perIp, lockout, lockout), perAccount);
    }
#else
    private Task<(LoginThrottleLimits Limits, int AccountMaxAttempts)> LimitsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult((_options.ToLimits(), _options.AccountMaxAttempts));
    }
#endif
}
