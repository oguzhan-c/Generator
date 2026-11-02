using CanFx.Security.Events;
using CanFx.Security.Sessions;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Services.AuthService;

/// <summary>Girişlerin ortak adımları ve güvenlik olayları.</summary>
internal static class AuthEvents
{
    /// <summary>
    /// Oturumu açar; cihaz daha önce görülmediyse "yeni cihazdan giriş" olayı yayınlanır (e-posta bildirimi bu olaya
    /// bağlıdır). Cihaz kimliği gönderilmeyen istemcilerde (tarayıcı ilk kez) olay yayınlanmaz.
    /// </summary>
    public static async Task<AuthResult> SignInAsync(
        AppUser user,
        ClientInfo client,
        string method,
        AuthTokenIssuer tokenIssuer,
        IUserSessionService sessions,
        ISecurityEvents events,
        CancellationToken cancellationToken)
    {
        bool newDevice = client.DeviceId is { Length: > 0 } deviceId
            && !await sessions.IsKnownDeviceAsync(user.Id.ToString(), deviceId, cancellationToken);

        AuthResult result = await tokenIssuer.IssueAsync(user, client, cancellationToken);

        var properties = new Dictionary<string, string?> { ["method"] = method };
        await events.PublishAsync(
            new SecurityEvent(SecurityEventTypes.LoginSucceeded, user.Id.ToString()) { SessionId = result.SessionId, DeviceId = client.DeviceId, Properties = properties },
            cancellationToken
        );

        if (newDevice)
        {
            await events.PublishAsync(
                new SecurityEvent(SecurityEventTypes.LoginFromNewDevice, user.Id.ToString())
                {
                    SessionId = result.SessionId,
                    DeviceId = client.DeviceId,
                    Properties = new Dictionary<string, string?> { ["deviceName"] = client.DeviceName, ["method"] = method },
                },
                cancellationToken
            );
        }

        return result;
    }

    /// <summary>Başarısız deneme; hesap bu denemeyle kilitlendiyse ayrıca kilitlenme olayı.</summary>
    public static async Task FailedAsync(ISecurityEvents events, AppUser user, DateTimeOffset now, string reason, CancellationToken cancellationToken, string type = SecurityEventTypes.LoginFailed)
    {
        await events.PublishAsync(new SecurityEvent(type, user.Id.ToString()) { Reason = reason }, cancellationToken);

        if (user.IsLockedOut(now))
            await events.PublishAsync(new SecurityEvent(SecurityEventTypes.AccountLocked, user.Id.ToString()) { Reason = reason }, cancellationToken);
    }
}
