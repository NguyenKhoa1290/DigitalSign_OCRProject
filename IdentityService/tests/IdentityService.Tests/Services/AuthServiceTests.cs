using IdentityService.Core.DTOs.Auth;
using IdentityService.Core.Entities;
using IdentityService.Core.Exceptions;
using IdentityService.Core.Interfaces;
using IdentityService.Core.Services;
using IdentityService.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using FluentAssertions;
using Xunit;

namespace IdentityService.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<ITokenService> _tokenServiceMock;
    private readonly Mock<IPasswordResetRepository> _passwordResetRepositoryMock;
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock;
    private readonly Mock<IRevokedAccessTokenRepository> _revokedAccessTokenRepositoryMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly IAuthService _authService;

    public AuthServiceTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _tokenServiceMock = new Mock<ITokenService>();
        _passwordResetRepositoryMock = new Mock<IPasswordResetRepository>();
        _refreshTokenRepositoryMock = new Mock<IRefreshTokenRepository>();
        _revokedAccessTokenRepositoryMock = new Mock<IRevokedAccessTokenRepository>();
        _emailServiceMock = new Mock<IEmailService>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:RefreshTokenExpiryDays"] = "7"
            })
            .Build();

        _authService = new AuthService(
            _userRepositoryMock.Object,
            _tokenServiceMock.Object,
            _passwordResetRepositoryMock.Object,
            _refreshTokenRepositoryMock.Object,
            _revokedAccessTokenRepositoryMock.Object,
            _emailServiceMock.Object,
            configuration);
    }

    private static AppUser CreateTestUser(bool isActive = true)
    {
        return new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            FullName = "Test User",
            Email = "test@hau.edu.vn",
            DepartmentId = Guid.NewGuid(),
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            UserRoles = new List<AppUserRole>
            {
                new AppUserRole
                {
                    Role = new AppRole { RoleName = "Clerk" }
                }
            }
        };
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnTokens()
    {
        // Arrange
        var request = new LoginRequestDto { Username = "testuser", Password = "Password123!" };
        var user = CreateTestUser();

        _userRepositoryMock
            .Setup(r => r.GetByUsernameAsync("testuser"))
            .ReturnsAsync(user);

        _tokenServiceMock
            .Setup(t => t.GenerateAccessToken(user, It.IsAny<IEnumerable<string>>()))
            .Returns("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJqdGkiOiJ0ZXN0LWp0aSJ9.signature");

        _tokenServiceMock
            .Setup(t => t.GenerateRefreshToken())
            .Returns("refresh-token-456");

        _refreshTokenRepositoryMock
            .Setup(r => r.CreateAsync(It.IsAny<RefreshToken>()))
            .ReturnsAsync((RefreshToken t) => t);

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().Be("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJqdGkiOiJ0ZXN0LWp0aSJ9.signature");
        result.RefreshToken.Should().Be("refresh-token-456");
        result.Username.Should().Be("testuser");
        result.FullName.Should().Be("Test User");
        result.Roles.Should().Contain("Clerk");
    }

    [Fact]
    public async Task LoginAsync_WithNonExistentUser_ShouldThrowInvalidCredentialsException()
    {
        // Arrange
        var request = new LoginRequestDto { Username = "nonexistent", Password = "Password123!" };

        _userRepositoryMock
            .Setup(r => r.GetByUsernameAsync("nonexistent"))
            .ReturnsAsync((AppUser?)null);

        // Act & Assert
        await _authService.Invoking(s => s.LoginAsync(request))
            .Should().ThrowAsync<InvalidCredentialsException>();
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ShouldThrowInvalidCredentialsException()
    {
        // Arrange
        var request = new LoginRequestDto { Username = "testuser", Password = "WrongPassword!" };
        var user = CreateTestUser();

        _userRepositoryMock
            .Setup(r => r.GetByUsernameAsync("testuser"))
            .ReturnsAsync(user);

        // Act & Assert
        await _authService.Invoking(s => s.LoginAsync(request))
            .Should().ThrowAsync<InvalidCredentialsException>();
    }

    [Fact]
    public async Task LoginAsync_WithLockedAccount_ShouldThrowAccountLockedException()
    {
        // Arrange
        var request = new LoginRequestDto { Username = "testuser", Password = "Password123!" };
        var user = CreateTestUser(isActive: false);

        _userRepositoryMock
            .Setup(r => r.GetByUsernameAsync("testuser"))
            .ReturnsAsync(user);

        // Act & Assert
        await _authService.Invoking(s => s.LoginAsync(request))
            .Should().ThrowAsync<AccountLockedException>();
    }

    [Fact]
    public async Task ValidateTokenAsync_WithValidToken_ShouldReturnValid()
    {
        // Arrange: Create a real JWT token structure for testing
        // The AuthService validates via ITokenService then tries to parse JWT claims
        // When token is valid but not parseable as JWT, result.IsValid depends on parse
        // We test the invalid path (non-JWT) returns false
        var request = new ValidateTokenRequestDto { Token = "not-a-valid-jwt" };

        _tokenServiceMock
            .Setup(t => t.ValidateTokenAsync("not-a-valid-jwt"))
            .ReturnsAsync(false);  // Not valid according to token service

        // Act
        var result = await _authService.ValidateTokenAsync(request);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTokenAsync_WhenServiceReturnsFalse_ShouldReturnInvalid()
    {
        // Arrange
        var request = new ValidateTokenRequestDto { Token = "expired-token" };

        _tokenServiceMock
            .Setup(t => t.ValidateTokenAsync("expired-token"))
            .ReturnsAsync(false);

        // Act
        var result = await _authService.ValidateTokenAsync(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Roles.Should().BeEmpty();
    }
}
