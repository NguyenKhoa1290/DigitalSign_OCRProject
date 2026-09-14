using IdentityService.Core.Entities;
using IdentityService.Core.Interfaces;
using IdentityService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation của IRoleRepository.
/// </summary>
public class RoleRepository : IRoleRepository
{
    private readonly AppDbContext _context;

    public RoleRepository(AppDbContext context) => _context = context;

    public async Task<IEnumerable<AppRole>> GetAllAsync()
        => await _context.AppRoles.OrderBy(r => r.RoleName).ToListAsync();

    public async Task<AppRole?> GetByIdAsync(Guid id)
        => await _context.AppRoles.FindAsync(id);

    public async Task<AppRole?> GetByNameAsync(string roleName)
        => await _context.AppRoles
            .FirstOrDefaultAsync(r => r.RoleName.ToLower() == roleName.ToLower());

    public async Task<AppRole> CreateAsync(AppRole role)
    {
        if (role.Id == Guid.Empty) role.Id = Guid.NewGuid();
        await _context.AppRoles.AddAsync(role);
        await _context.SaveChangesAsync();
        return role;
    }

    public async Task<AppRole> UpdateAsync(AppRole role)
    {
        _context.AppRoles.Update(role);
        await _context.SaveChangesAsync();
        return role;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var role = await _context.AppRoles.FindAsync(id);
        if (role is null) return false;
        _context.AppRoles.Remove(role);
        await _context.SaveChangesAsync();
        return true;
    }
}
