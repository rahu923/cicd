using cicd.Data;
using cicd.Security;
using Microsoft.EntityFrameworkCore;

namespace cicd.Repositories;

public class UserRepository(ApplicationDbContext context) : IUserRepository
{
    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(int page = 1, CancellationToken cancellationToken = default)
    {
        page = Math.Clamp(page, 1, 10000);
        var users = await context.Users.AsNoTracking().OrderBy(x => x.Email).ThenBy(x => x.Id)
            .Skip((page - 1) * 50).Take(50)
            .Select(x => new { x.Id, x.Email }).ToListAsync(cancellationToken);
        var ids = users.Select(x => x.Id).ToArray();
        var roles = await (from userRole in context.UserRoles.AsNoTracking()
                           join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                           where ids.Contains(userRole.UserId)
                           select new { userRole.UserId, role.Name }).ToListAsync(cancellationToken);
        return users.Select(x => new UserSummary(x.Id, x.Email ?? "", roles
            .Where(r => r.UserId == x.Id).Select(r => r.Name!).Order().ToArray())).ToArray();
    }

    public async Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await context.Roles.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var claims = await context.RoleClaims.AsNoTracking()
            .Where(x => x.ClaimType == Permissions.ClaimType).ToListAsync(cancellationToken);
        return roles.Select(x => new RoleSummary(x.Name!, claims.Where(c => c.RoleId == x.Id)
            .Select(c => c.ClaimValue!).Order().ToArray())).ToArray();
    }
}
