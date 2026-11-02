using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Application.Services.AuthService;

/// <summary>Kurtarma kodları: yalnızca bir kez gösterilir, sunucuda HMAC'leri saklanır.</summary>
public sealed record RecoveryCodesDto(IReadOnlyList<string> Codes);
