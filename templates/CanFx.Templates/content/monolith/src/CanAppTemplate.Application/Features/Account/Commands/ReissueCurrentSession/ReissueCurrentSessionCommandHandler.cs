using CanFx.Mediator;
using CanFx.Persistence.Repositories;
using CanFx.Security.Entities;
using CanFx.Security.Tokens;
using CanAppTemplate.Application.Features.Auth.Rules;
using CanAppTemplate.Application.Services.AuthService;
using CanAppTemplate.Domain.Entities;

namespace CanAppTemplate.Application.Features.Account.Commands.ReissueCurrentSession;

public sealed class ReissueCurrentSessionCommandHandler : IRequestHandler<ReissueCurrentSessionCommand, AuthResult>
{
    private readonly ITokenService _tokens;
    private readonly IRepository<RefreshToken<IdentityKey>, IdentityKey> _refreshTokens;
    private readonly AuthTokenIssuer _tokenIssuer;
    private readonly ISecurityStampValidator? _stampValidator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly AuthBusinessRules _authBusinessRules;

    public ReissueCurrentSessionCommandHandler(
        ITokenService tokens,
        IRepository<RefreshToken<IdentityKey>, IdentityKey> refreshTokens,
        AuthTokenIssuer tokenIssuer,
        IEnumerable<ISecurityStampValidator> stampValidators,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        AuthBusinessRules authBusinessRules)
    {
        _tokens = tokens;
        _refreshTokens = refreshTokens;
        _tokenIssuer = tokenIssuer;
        _stampValidator = stampValidators.FirstOrDefault();
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _authBusinessRules = authBusinessRules;
    }

    public async Task<AuthResult> Handle(ReissueCurrentSessionCommand request, CancellationToken cancellationToken)
    {
        IdentityKey userId = _authBusinessRules.CurrentUserIdShouldBeKnown();

        // Diğer sunuculardaki önbellek de en geç önbellek süresi sonunda yeni damgayı görür.
        _stampValidator?.Invalidate(userId.ToString());

        _authBusinessRules.SessionShouldBeActive(!string.IsNullOrEmpty(request.RefreshToken));

        string hash = _tokens.HashRefreshToken(request.RefreshToken);
        RefreshToken<IdentityKey>? token = await _refreshTokens.GetAsync(t => t.TokenHash == hash && t.UserId == userId, cancellationToken: cancellationToken);
        _authBusinessRules.SessionShouldBeActive(token is not null && token.IsActive(_timeProvider.GetUtcNow()));

        AppUser user = await _authBusinessRules.CurrentUserShouldExist(q => q.WithRolesAndPermissions(), cancellationToken: cancellationToken);
        AuthResult session = await _tokenIssuer.RotateAsync(user, token, request.Client, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return session;
    }
}
