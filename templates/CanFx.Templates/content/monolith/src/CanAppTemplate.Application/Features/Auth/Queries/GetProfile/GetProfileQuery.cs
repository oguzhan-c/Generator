using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Auth.Queries.GetProfile;

/// <summary>Giriş yapmış kullanıcının profili (<c>GET /api/auth/me</c>).</summary>
public sealed record GetProfileQuery : IRequest<UserProfileDto>, ISecuredRequest;
