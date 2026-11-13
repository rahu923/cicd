using cicd.Repositories;
using cicd.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace cicd.Controllers;

[Authorize(Policy = Permissions.RolesView)]
public class RolesController(IUserRepository users) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await users.GetRolesAsync(cancellationToken));
}
