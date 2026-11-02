using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Application.Features.Account.Commands.DeletePasskey;

public sealed record DeletePasskeyCommand(IdentityKey Id) : IRequest, ISecuredRequest, ITransactionalRequest, ILoggableRequest;
