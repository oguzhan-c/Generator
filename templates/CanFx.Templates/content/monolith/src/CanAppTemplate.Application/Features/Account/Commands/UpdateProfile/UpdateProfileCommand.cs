using CanFx.Application;
using CanFx.Mediator;

namespace CanAppTemplate.Application.Features.Account.Commands.UpdateProfile;

/// <summary>Ad, soyad, telefon ve kampanya izni.</summary>
public sealed record UpdateProfileCommand(string FirstName, string LastName, string? PhoneNumber, bool MarketingConsent)
    : IRequest, ISecuredRequest, ITransactionalRequest;
