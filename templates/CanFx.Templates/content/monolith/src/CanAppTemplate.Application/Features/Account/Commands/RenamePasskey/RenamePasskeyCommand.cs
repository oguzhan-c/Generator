using CanFx.Application;
using CanFx.Mediator;
using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Application.Features.Account.Commands.RenamePasskey;

public sealed record RenamePasskeyCommand(IdentityKey Id, string Name) : IRequest, ISecuredRequest, ITransactionalRequest;
