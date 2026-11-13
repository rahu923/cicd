using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace cicd.Data;

public class IdentityDatabaseHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Query Identity tables to catch a reachable database whose schema is missing.
        await database.Users.AnyAsync(cancellationToken);
        var rolesExist = await database.Roles.AnyAsync(cancellationToken);
        return rolesExist ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Identity is not initialized.");
    }
}
