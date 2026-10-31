using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobFlow.Tests;

/// <summary>
/// The API docs are on in Development and off in any other environment unless
/// ENABLE_SWAGGER=true, so a production deploy does not publish its schema.
/// </summary>
public class SwaggerExposureTests
{
    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger/index.html")]
    public async Task Docs_AreServed_InDevelopment(string path)
    {
        var res = await new TestHost().CreateAnonClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger/index.html")]
    public async Task Docs_AreHidden_InProduction_ByDefault(string path)
    {
        _ = new TestHost(); // sets the in-memory database and signing key variables
        using var factory = new WebApplicationFactory<JobFlowApi>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        var res = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
