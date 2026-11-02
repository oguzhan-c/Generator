using CanFx.Security.Entities;

namespace CanAppTemplate.Application.Services.AuthService;

/// <summary>İkinci adım için istemciye gösterilecek bilgi.</summary>
public sealed record TwoFactorPrompt(AuthenticatorType Method, string? Destination);
