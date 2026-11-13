using System.Security.Claims;
using cicd.Data;
using cicd.Models;
using cicd.Repositories;
using cicd.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace cicdtest;

public class IdentityTests
{
    private static ServiceProvider CreateServices(bool enableSeed = true, string? password = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddIdentityCore<ApplicationUser>(o =>
        {
            o.User.RequireUniqueEmail = true;
            o.Password.RequiredLength = 12;
        }).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddAuthorization(Permissions.Configure);
        services.Configure<IdentitySeedOptions>(o =>
        {
            o.Enabled = enableSeed;
            // Generated per test, never a reusable application credential.
            var generatedPassword = password ?? $"Aa1!{Guid.NewGuid():N}";
            o.Users =
            [
                new() { Email = "admin@example.com", Role = AppRoles.Admin, Password = generatedPassword },
                new() { Email = "customer@example.com", Role = AppRoles.Customer, Password = generatedPassword }
            ];
        });
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<IUserRepository, UserRepository>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task SeedIsRepeatableAndPreservesExistingPasswords()
    {
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await seeder.SeedAsync();
        var admin = (await users.FindByEmailAsync("admin@example.com"))!;
        var hash = admin.PasswordHash;
        await seeder.SeedAsync();
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(3, await db.Roles.CountAsync());
        Assert.Equal(6, await db.RoleClaims.CountAsync());
        Assert.Equal(hash, admin.PasswordHash);
        Assert.True(await users.IsInRoleAsync(admin, AppRoles.Admin));
        var customer = (await users.FindByEmailAsync("customer@example.com"))!;
        Assert.True(await users.IsInRoleAsync(customer, AppRoles.Customer));
        Assert.False(await users.IsInRoleAsync(customer, AppRoles.Admin));
    }

    [Theory]
    [InlineData("admin@example.com", Permissions.UsersView, true)]
    [InlineData("admin@example.com", Permissions.RolesView, true)]
    [InlineData("customer@example.com", Permissions.UsersView, false)]
    [InlineData("customer@example.com", Permissions.RolesView, false)]
    [InlineData("customer@example.com", Permissions.ProfileView, true)]
    public async Task RoleClaimsEnforcePermissionPolicies(string email, string policy, bool expected)
    {
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var factory = new UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(users, roles,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>());
        var principal = await factory.CreateAsync((await users.FindByEmailAsync(email))!);
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        Assert.Equal(expected, (await authorization.AuthorizeAsync(principal, policy)).Succeeded);
    }

    [Fact]
    public async Task ManagerHasUserAccessButCannotViewRolePermissions()
    {
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var claims = await roles.GetClaimsAsync((await roles.FindByNameAsync(AppRoles.Manager))!);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        Assert.True((await authorization.AuthorizeAsync(principal, Permissions.UsersView)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(principal, Permissions.RolesView)).Succeeded);
    }

    [Fact]
    public async Task AnonymousCannotUsePermissionClaim()
    {
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Permissions.ClaimType, Permissions.UsersView)]));
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        Assert.False((await authorization.AuthorizeAsync(principal, Permissions.UsersView)).Succeeded);
    }

    [Fact]
    public async Task DisabledUserSeedingStillCreatesRolesAndPermissions()
    {
        await using var provider = CreateServices(enableSeed: false);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Equal(3, await db.Roles.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("weak")]
    public async Task InvalidSeedPasswordFailsBeforeCreatingUsersOrRoles(string password)
    {
        await using var provider = CreateServices(password: password);
        await using var scope = provider.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync());
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Roles.ToListAsync());
    }

    [Fact]
    public async Task RepositoryReturnsUserRolesAndRolePermissions()
    {
        await using var provider = CreateServices();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var users = await repository.GetUsersAsync();
        Assert.Equal(2, users.Count);
        Assert.Contains(AppRoles.Customer, users.Single(x => x.Email == "customer@example.com").Roles);
        var roles = await repository.GetRolesAsync();
        Assert.Contains(Permissions.RolesView, roles.Single(x => x.Name == AppRoles.Admin).Permissions);
        Assert.Equal(new[] { Permissions.ProfileView }, roles.Single(x => x.Name == AppRoles.Customer).Permissions);
    }
}
