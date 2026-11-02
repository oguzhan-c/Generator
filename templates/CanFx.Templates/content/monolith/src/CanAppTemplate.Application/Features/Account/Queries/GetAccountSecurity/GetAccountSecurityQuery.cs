using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;
using CanAppTemplate.Application.Services.AuthService;

namespace CanAppTemplate.Application.Features.Account.Queries.GetAccountSecurity;

public sealed record GetAccountSecurityQuery : IRequest<AccountSecurityDto>, ISecuredRequest;
