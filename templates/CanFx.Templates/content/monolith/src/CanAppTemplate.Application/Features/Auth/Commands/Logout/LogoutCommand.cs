using CanFx.Mediator;
using CanFx.Security.Entities;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Auth.Commands.Logout;

/// <summary>Çıkış: bu oturumun refresh token'ı kapatılır (access token'ı ve cookie'leri WebApi geri alır).</summary>
public sealed record LogoutCommand(string? RefreshToken, ClientInfo Client) : IRequest;
