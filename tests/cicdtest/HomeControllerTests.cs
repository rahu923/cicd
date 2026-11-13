using cicd.Controllers;
using cicd.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace cicdtest;

public class HomeControllerTests
{
    [Fact]
    public void IndexReturnsView()
    {
        var controller = new HomeController(NullLogger<HomeController>.Instance);
        Assert.IsType<ViewResult>(controller.Index());
    }

    [Fact]
    public void ErrorIncludesRequestIdentifier()
    {
        var controller = new HomeController(NullLogger<HomeController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { TraceIdentifier = "test-request" }
            }
        };
        var view = Assert.IsType<ViewResult>(controller.Error());
        var model = Assert.IsType<ErrorViewModel>(view.Model);
        Assert.False(string.IsNullOrWhiteSpace(model.RequestId));
        Assert.True(model.ShowRequestId);
    }
}
