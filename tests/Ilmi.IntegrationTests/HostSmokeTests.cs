using Microsoft.AspNetCore.Mvc.Testing;

namespace Ilmi.IntegrationTests;

/// <summary>Proves the API host composes and starts. Module behaviour is tested in each module's own test project.</summary>
public class HostSmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void Host_Starts()
    {
        // The host requires a connection string (never committed). A placeholder is enough to start: no request opens a connection.
        using var configured = factory.WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:UsersIdentity", "Server=smoke-test-never-connected;Database=Ilmi"));
        Assert.NotNull(configured.CreateClient());
    }
}
