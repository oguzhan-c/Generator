using Fido2NetLib;
using Microsoft.Extensions.DependencyInjection;

namespace CanAppTemplate.Application.Services.AuthService;

public static class PasskeyLimits
{
    public const int MaxPerUser = 10;
    public const int NameMaxLength = 64;
}
