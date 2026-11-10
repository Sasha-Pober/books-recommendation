using BookStore.Domain.Common;
using BookStore.Domain.Exceptions;
using BookStore.Domain.Identity;
using BookStore.Domain.Repositories;
using BookStore.Presentation.Auth;
using BookStore.Services.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BookStore.UnitTests.Auth;

public class AuthServiceTests
{
    private const string SessionId = "session-1";

    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IInteractionRepository _interactions = Substitute.For<IInteractionRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly UserAccount _user = new(Guid.NewGuid(), "a@b.c", "Reader", [Roles.Customer]);

    public AuthServiceTests()
    {
        _currentUser.SessionId.Returns(SessionId);
        _tokens.CreateAccessToken(default!).ReturnsForAnyArgs(new AccessToken("access", DateTime.UtcNow));
        _tokens.CreateRefreshTokenAsync(default, default).ReturnsForAnyArgs(new RefreshToken("refresh", DateTime.UtcNow));
    }

    [Fact]
    public async Task Login_WithValidCredentials_IssuesTokens_AndAttachesGuestEvents()
    {
        _identity.ValidateCredentialsAsync("a@b.c", "secret123", Arg.Any<CancellationToken>()).Returns(_user);

        var response = await CreateService().LoginAsync(new LoginRequest(" a@b.c ", "secret123"), CancellationToken.None);

        Assert.Equal("access", response.AccessToken);
        Assert.Equal("refresh", response.RefreshToken);
        Assert.Equal(_user.Id, response.User.Id);
        await _interactions.Received(1).AssignGuestSessionAsync(SessionId, _user.Id, Arg.Any<CancellationToken>());
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ThrowsUnauthorized_AndIssuesNothing()
    {
        _identity.ValidateCredentialsAsync(default!, default!, default).ReturnsForAnyArgs((UserAccount?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService().LoginAsync(new LoginRequest("a@b.c", "wrong"), CancellationToken.None));

        await _tokens.DidNotReceiveWithAnyArgs().CreateRefreshTokenAsync(default, default);
        await _interactions.DidNotReceiveWithAnyArgs().AssignGuestSessionAsync(default!, default, default);
    }

    [Fact]
    public async Task Register_CreatesCustomer()
    {
        _identity.RegisterAsync(default!, default!, default!, default!, default).ReturnsForAnyArgs(_user);

        await CreateService().RegisterAsync(new RegisterRequest("a@b.c", "secret123", "Reader"), CancellationToken.None);

        await _identity.Received(1).RegisterAsync("a@b.c", "secret123", "Reader", Roles.Customer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_WithRejectedToken_SavesRevocations_AndThrowsUnauthorized()
    {
        _tokens.RedeemRefreshTokenAsync("stolen", Arg.Any<CancellationToken>()).Returns((Guid?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            CreateService().RefreshAsync(new RefreshTokenRequest("stolen"), CancellationToken.None));

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _tokens.DidNotReceiveWithAnyArgs().CreateAccessToken(default!);
    }

    [Fact]
    public async Task Refresh_WithValidToken_IssuesNewPair_WithCurrentRoles()
    {
        var promoted = _user with { Roles = [Roles.Manager] };
        _tokens.RedeemRefreshTokenAsync("valid", Arg.Any<CancellationToken>()).Returns(_user.Id);
        _identity.FindByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(promoted);

        var response = await CreateService().RefreshAsync(new RefreshTokenRequest("valid"), CancellationToken.None);

        Assert.Equal([Roles.Manager], response.User.Roles);
        _tokens.Received(1).CreateAccessToken(promoted);
    }

    private AuthService CreateService() =>
        new(_identity, _tokens, _interactions, _currentUser, _unitOfWork, NullLogger<AuthService>.Instance);
}
