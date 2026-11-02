using CanFx.Security.Entities;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Application.Services.AuthService;

/// <summary>Hesaba bağlı dış hesap (Google, Microsoft, GitHub ...).</summary>
public sealed record ExternalLoginDto(string Provider, string? DisplayName, DateTimeOffset CreatedAt)
{
    public static ExternalLoginDto From(UserLogin<IdentityKey> login) => new(login.LoginProvider, login.ProviderDisplayName, login.CreatedAt);
}
