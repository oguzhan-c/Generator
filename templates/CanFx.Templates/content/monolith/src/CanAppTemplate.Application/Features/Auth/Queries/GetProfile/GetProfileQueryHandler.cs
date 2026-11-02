using CanFx.Mediator;
using CanAppTemplate.Application.Features.Auth.Rules;
using CanAppTemplate.Application.Services.AuthService;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Features.Auth.Queries.GetProfile;

public sealed class GetProfileQueryHandler : IRequestHandler<GetProfileQuery, UserProfileDto>
{
    private readonly AuthBusinessRules _authBusinessRules;

    public GetProfileQueryHandler(AuthBusinessRules authBusinessRules) => _authBusinessRules = authBusinessRules;

    public async Task<UserProfileDto> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        AppUser user = await _authBusinessRules.CurrentUserShouldExist(q => q.WithRolesAndPermissions(), enableTracking: false, cancellationToken);
        return UserProfileDto.From(user);
    }
}
