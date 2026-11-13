using Microsoft.AspNetCore.Authorization;

namespace cicd.Security;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Customer = "Customer";
}

public static class Permissions
{
    public const string ClaimType = "permission";
    public const string ProfileView = "profile.view";
    public const string UsersView = "users.view";
    public const string RolesView = "roles.view";

    public static IReadOnlyDictionary<string, string[]> RolePermissions { get; } =
        new Dictionary<string, string[]>
        {
            [AppRoles.Admin] = [ProfileView, UsersView, RolesView],
            [AppRoles.Manager] = [ProfileView, UsersView],
            [AppRoles.Customer] = [ProfileView]
        };

    public static void Configure(AuthorizationOptions options)
    {
        foreach (var permission in RolePermissions.Values.SelectMany(x => x).Distinct())
        {
            options.AddPolicy(permission, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ClaimType, permission));
        }
    }
}
