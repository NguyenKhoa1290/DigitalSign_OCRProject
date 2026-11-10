using IdentityService.Core.Entities;
using IdentityService.Core.Exceptions;
using IdentityService.Infrastructure.Data;
using IdentityService.Infrastructure.Repositories;
using IdentityService.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Tests.Services;

public class ThemePreferenceServiceTests
{
    [Fact]
    public async Task StaleProfileUpdateCannotResetAThemeSavedByAnotherSession()
    {
        var options=new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var id=Guid.NewGuid();
        await using(var seed=new AppDbContext(options)) { seed.AppUsers.Add(new AppUser{Id=id,Username="theme",FullName="Original",IsActive=true});await seed.SaveChangesAsync(); }
        await using var stale=new AppDbContext(options);var repository=new UserRepository(stale);
        var old=(await repository.GetByIdAsync(id))!;
        await using(var newer=new AppDbContext(options))await new ThemePreferenceService(newer).SetAsync(id,"dark",default);
        old.FullName="Updated profile";await repository.UpdateAsync(old);
        await using var verify=new AppDbContext(options);var user=await verify.AppUsers.SingleAsync(u=>u.Id==id);
        Assert.Equal("dark",user.Theme);Assert.Equal("Updated profile",user.FullName);
    }
    [Fact]
    public async Task InactiveUserCannotSaveTheme()
    {
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var id=Guid.NewGuid();db.AppUsers.Add(new AppUser{Id=id,Username="inactive",IsActive=false});await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UserNotFoundException>(()=>new ThemePreferenceService(db).SetAsync(id,"dark",default));
        Assert.Equal("light",(await db.AppUsers.SingleAsync()).Theme);
    }
}
