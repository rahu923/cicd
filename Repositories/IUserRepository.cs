namespace cicd.Repositories;

public record UserSummary(string Id, string Email, IReadOnlyList<string> Roles);
public record RoleSummary(string Name, IReadOnlyList<string> Permissions);

// Identity writes go through UserManager/RoleManager to preserve hashing and validation.
public interface IUserRepository
{
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(int page = 1, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken cancellationToken = default);
}
