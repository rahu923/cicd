using cicd.Repositories;
using cicd.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace cicd.Controllers;

[Authorize(Policy = Permissions.UsersView)]
public class UsersController(IUserRepository users) : Controller
{
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        ViewData["Page"] = Math.Clamp(page, 1, 10000);
        return View(await users.GetUsersAsync(page, cancellationToken));
    }
}
