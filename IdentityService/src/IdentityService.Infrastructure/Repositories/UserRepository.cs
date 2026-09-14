using IdentityService.Core.Entities;
using IdentityService.Core.Interfaces;
using IdentityService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation của IUserRepository.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context) => _context = context;

    // ── Read ─────────────────────────────────────────────────────────────────

    public async Task<AppUser?> GetByIdAsync(Guid id)
        => await _context.AppUsers
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == id);

    public async Task<AppUser?> GetByUsernameAsync(string username)
        => await _context.AppUsers
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Username == username);

    public async Task<AppUser?> GetByEmailAsync(string email)
        => await _context.AppUsers
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Email == email);

    public async Task<(IEnumerable<AppUser> Users, int Total)> GetAllAsync(int page, int pageSize, string? search)
    {
        var query = _context.AppUsers
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.Department)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u =>
                u.Username.ToLower().Contains(term) ||
                u.FullName.ToLower().Contains(term) ||
                (u.Email != null && u.Email.ToLower().Contains(term)));
        }

        var total = await query.CountAsync();
        var users = await query
            .OrderBy(u => u.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (users, total);
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task<AppUser> CreateAsync(AppUser user)
    {
        if (user.Id == Guid.Empty) user.Id = Guid.NewGuid();
        user.CreatedAt = DateTime.UtcNow;
        await _context.AppUsers.AddAsync(user);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(user.Id))!;
    }

    public async Task<AppUser> UpdateAsync(AppUser user)
    {
        _context.AppUsers.Update(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var user = await _context.AppUsers.FindAsync(id);
        if (user is null) return false;
        _context.AppUsers.Remove(user);
        await _context.SaveChangesAsync();
        return true;
    }

    // ── Role Management ───────────────────────────────────────────────────────

    public async Task<bool> AssignRoleAsync(Guid userId, Guid roleId)
    {
        var exists = await _context.AppUserRoles
            .AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
        if (exists) return true;

        await _context.AppUserRoles.AddAsync(new AppUserRole { UserId = userId, RoleId = roleId });
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveRoleAsync(Guid userId, Guid roleId)
    {
        var userRole = await _context.AppUserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
        if (userRole is null) return false;
        _context.AppUserRoles.Remove(userRole);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<string>> GetUserRolesAsync(Guid userId)
        => await _context.AppUserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.RoleName)
            .ToListAsync();
}
