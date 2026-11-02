using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Application.Services.AuthService;

/// <summary>Authenticator uygulamasına eklenecek bilgiler: QR kodu (<see cref="ProvisioningUri"/>) ya da elle girilecek anahtar.</summary>
public sealed record OtpSetupDto(string Secret, string ProvisioningUri);
