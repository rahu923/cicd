using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using cicd.Models;
using cicd.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace cicd.Data;

public class IdentitySeedOptions
{
    public bool Enabled { get; set; }
    public List<SeedUserOptions> Users { get; set; } = [];
}

public class SeedUserOptions
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "";
}

public class IdentitySeeder(
    RoleManager<IdentityRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IOptions<IdentitySeedOptions> options)
{
    public async Task SeedAsync()
    {
        var settings = options.Value;
        if (settings.Enabled)
        {
            if (settings.Users.Count != 2 || settings.Users.Select(x => x.Email)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
                throw new InvalidOperationException("IdentitySeed must define two different users.");

            foreach (var user in settings.Users)
            {
                if (!new EmailAddressAttribute().IsValid(user.Email) ||
                    !Permissions.RolePermissions.ContainsKey(user.Role))
                    throw new InvalidOperationException("Each seed user needs a valid email and an Admin, Manager, or Customer role.");
                if (await userManager.FindByEmailAsync(user.Email) is null)
                {
                    if (string.IsNullOrWhiteSpace(user.Password))
                        throw new InvalidOperationException("Supply seed passwords through user secrets or Azure application settings.");
                    // Validate all new passwords before making any changes.
                    var candidate = new ApplicationUser { UserName = user.Email, Email = user.Email };
                    foreach (var validator in userManager.PasswordValidators)
                        EnsureSuccess(await validator.ValidateAsync(userManager, candidate, user.Password));
                }
            }
        }

        foreach (var (name, permissions) in Permissions.RolePermissions)
        {
            var role = await roleManager.FindByNameAsync(name);
            if (role is null)
            {
                role = new IdentityRole(name);
                EnsureSuccess(await roleManager.CreateAsync(role));
            }

            var claims = await roleManager.GetClaimsAsync(role);
            foreach (var permission in permissions)
                if (!claims.Any(x => x.Type == Permissions.ClaimType && x.Value == permission))
                    EnsureSuccess(await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission)));
        }

        if (!settings.Enabled) return;
        foreach (var configured in settings.Users)
        {
            var user = await userManager.FindByEmailAsync(configured.Email);
            if (user is null)
            {
                user = new ApplicationUser { UserName = configured.Email, Email = configured.Email };
                EnsureSuccess(await userManager.CreateAsync(user, configured.Password));
            }
            if (!await userManager.IsInRoleAsync(user, configured.Role))
                EnsureSuccess(await userManager.AddToRoleAsync(user, configured.Role));
        }
    }

    private static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Identity initialization failed: " +
                string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}
