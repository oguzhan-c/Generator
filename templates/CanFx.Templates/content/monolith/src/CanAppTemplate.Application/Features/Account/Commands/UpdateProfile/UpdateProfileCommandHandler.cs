using CanFx.Application;
using CanFx.Mediator;
using CanAppTemplate.Application.Features.Auth.Rules;
using CanAppTemplate.Domain.Entities;
using CanAppTemplate.Application.Services.Users;

namespace CanAppTemplate.Application.Features.Account.Commands.UpdateProfile;

public sealed class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand>
{
    private readonly AuthBusinessRules _authBusinessRules;

    public UpdateProfileCommandHandler(AuthBusinessRules authBusinessRules) => _authBusinessRules = authBusinessRules;

    public async Task Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        AppUser user = await _authBusinessRules.CurrentUserShouldExist(cancellationToken: cancellationToken);
        user.UpdateProfile(request.FirstName, request.LastName, request.PhoneNumber, request.MarketingConsent).OrThrow();
    }
}
