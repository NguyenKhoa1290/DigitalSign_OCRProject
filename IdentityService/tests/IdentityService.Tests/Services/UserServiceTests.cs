using IdentityService.Core.DTOs.Users;
using IdentityService.Core.Entities;
using IdentityService.Core.Exceptions;
using IdentityService.Core.Interfaces;
using IdentityService.Core.Services;
using IdentityService.Infrastructure.Services;
using Moq;
using FluentAssertions;
using Xunit;

namespace IdentityService.Tests.Services;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IDepartmentRepository> _departmentRepositoryMock;
    private readonly IUserService _userService;

    public UserServiceTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _departmentRepositoryMock = new Mock<IDepartmentRepository>();
        _userService = new UserService(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _departmentRepositoryMock.Object);
    }

    private static AppUser CreateTestUser()
    {
        var deptId = Guid.NewGuid();
        return new AppUser
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            PasswordHash = "hashed_password",
            FullName = "Nguyễn Văn A",
            Email = "nva@hau.edu.vn",
            PhoneNumber = "0912345678",
            DepartmentId = deptId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UserRoles = new List<AppUserRole>
            {
                new AppUserRole { Role = new AppRole { Id = Guid.NewGuid(), RoleName = "Clerk" } }
            }
        };
    }

    [Fact]
    public async Task GetUserByIdAsync_WithExistingUser_ShouldReturnUserDto()
    {
        // Arrange
        var user = CreateTestUser();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _departmentRepositoryMock.Setup(r => r.GetByIdAsync(user.DepartmentId!.Value))
            .ReturnsAsync(new Department { Id = user.DepartmentId!.Value, DeptName = "Phòng Tổng hợp", DeptCode = "TH" });

        // Act
        var result = await _userService.GetUserByIdAsync(user.Id);

        // Assert
        result.Should().NotBeNull();
        result.Username.Should().Be("testuser");
        result.FullName.Should().Be("Nguyễn Văn A");
        result.Roles.Should().Contain("Clerk");
    }

    [Fact]
    public async Task GetUserByIdAsync_WithNonExistentUser_ShouldThrowUserNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(nonExistentId)).ReturnsAsync((AppUser?)null);

        // Act & Assert
        await _userService.Invoking(s => s.GetUserByIdAsync(nonExistentId))
            .Should().ThrowAsync<UserNotFoundException>();
    }

    [Fact]
    public async Task CreateUserAsync_WithValidData_ShouldCreateUser()
    {
        // Arrange
        var departmentId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var request = new CreateUserDto
        {
            Username = "newuser",
            Password = "StrongPass@123",
            FullName = "Trần Thị B",
            Email = "ttb@hau.edu.vn",
            PhoneNumber = "0987654321",
            DepartmentId = departmentId,
            RoleIds = new List<Guid> { roleId }
        };

        _userRepositoryMock.Setup(r => r.GetByUsernameAsync("newuser")).ReturnsAsync((AppUser?)null);
        _userRepositoryMock.Setup(r => r.GetByEmailAsync("ttb@hau.edu.vn")).ReturnsAsync((AppUser?)null);
        _departmentRepositoryMock.Setup(r => r.GetByIdAsync(departmentId))
            .ReturnsAsync(new Department { Id = departmentId, DeptName = "Phòng Tổng hợp", DeptCode = "TH" });
        _roleRepositoryMock.Setup(r => r.GetByIdAsync(roleId))
            .ReturnsAsync(new AppRole { Id = roleId, RoleName = "Clerk" });

        _userRepositoryMock.Setup(r => r.CreateAsync(It.IsAny<AppUser>()))
            .ReturnsAsync((AppUser u) =>
            {
                u.UserRoles = new List<AppUserRole> { new AppUserRole { Role = new AppRole { RoleName = "Clerk" } } };
                return u;
            });

        // Act
        var result = await _userService.CreateUserAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Username.Should().Be("newuser");
        result.FullName.Should().Be("Trần Thị B");
        _userRepositoryMock.Verify(r => r.CreateAsync(It.Is<AppUser>(u =>
            u.Username == "newuser" &&
            !string.IsNullOrEmpty(u.PasswordHash))), Times.Once);
    }

    [Fact]
    public async Task CreateUserAsync_WithDuplicateUsername_ShouldThrowUserAlreadyExistsException()
    {
        // Arrange
        var request = new CreateUserDto
        {
            Username = "existinguser",
            Password = "Pass@123",
            FullName = "User A",
            Email = "unique@hau.edu.vn",
            DepartmentId = Guid.NewGuid()
        };

        _userRepositoryMock.Setup(r => r.GetByUsernameAsync("existinguser"))
            .ReturnsAsync(new AppUser { Username = "existinguser" });

        // Act & Assert
        await _userService.Invoking(s => s.CreateUserAsync(request))
            .Should().ThrowAsync<UserAlreadyExistsException>();
    }

    [Fact]
    public async Task GetAllUsersAsync_ShouldReturnPagedResult()
    {
        // Arrange
        var users = new List<AppUser>
        {
            CreateTestUser(),
            CreateTestUser()
        };
        _userRepositoryMock.Setup(r => r.GetAllAsync(1, 10, null))
            .ReturnsAsync((users, 2));

        // Act
        var result = await _userService.GetAllUsersAsync(1, 10, null);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task DeleteUserAsync_WithExistingUser_ShouldReturnTrue()
    {
        // Arrange
        var user = CreateTestUser();
        _userRepositoryMock.Setup(r => r.GetByIdAsync(user.Id)).ReturnsAsync(user);
        _userRepositoryMock.Setup(r => r.DeleteAsync(user.Id)).ReturnsAsync(true);

        // Act
        var result = await _userService.DeleteUserAsync(user.Id);

        // Assert
        result.Should().BeTrue();
        _userRepositoryMock.Verify(r => r.DeleteAsync(user.Id), Times.Once);
    }
}
