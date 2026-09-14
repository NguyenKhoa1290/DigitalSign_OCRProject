using IdentityService.Core.Entities;
using IdentityService.Core.Interfaces;
using IdentityService.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation của IDepartmentRepository.
/// </summary>
public class DepartmentRepository : IDepartmentRepository
{
    private readonly AppDbContext _context;

    public DepartmentRepository(AppDbContext context) => _context = context;

    public async Task<IEnumerable<Department>> GetAllAsync()
        => await _context.Departments
            .Include(d => d.Parent)
            .OrderBy(d => d.DeptName)
            .ToListAsync();

    public async Task<Department?> GetByIdAsync(Guid id)
        => await _context.Departments
            .Include(d => d.Parent)
            .Include(d => d.Children)
            .FirstOrDefaultAsync(d => d.Id == id);

    public async Task<Department> CreateAsync(Department department)
    {
        if (department.Id == Guid.Empty) department.Id = Guid.NewGuid();
        department.CreatedAt = DateTime.UtcNow;
        await _context.Departments.AddAsync(department);
        await _context.SaveChangesAsync();
        return (await GetByIdAsync(department.Id))!;
    }

    public async Task<Department> UpdateAsync(Department department)
    {
        _context.Departments.Update(department);
        await _context.SaveChangesAsync();
        return department;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var dept = await _context.Departments.FindAsync(id);
        if (dept is null) return false;
        _context.Departments.Remove(dept);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<Department>> GetChildrenAsync(Guid? parentId)
        => await _context.Departments
            .Where(d => d.ParentId == parentId)
            .Include(d => d.Children)
            .OrderBy(d => d.DeptName)
            .ToListAsync();
}
