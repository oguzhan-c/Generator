using CanFx.Security.Entities;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Application.Services.AuthService;

public sealed record PasskeyDto(IdentityKey Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, bool IsBackedUp)
{
    public static PasskeyDto From(UserPasskey<IdentityKey> passkey) => new(passkey.Id, passkey.Name, passkey.CreatedAt, passkey.LastUsedAt, passkey.IsBackedUp);
}
