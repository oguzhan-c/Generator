using CanFx.Mediator;
using CanFx.Security.Entities;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Auth.Commands.RefreshToken;

/// <summary>
/// Refresh token ile yeni token çifti (rotasyon; oturum aynı kalır). Kapatılmış bir token tekrar kullanılırsa token
/// çalınmış sayılır: kullanıcının tüm oturumları kapatılır ve kritik güvenlik olayı yayınlanır.
/// </summary>
public sealed record RefreshTokenCommand(string RefreshToken, ClientInfo Client) : IRequest<AuthResult>;
