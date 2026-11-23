using Microsoft.AspNetCore.Mvc.Testing;

namespace Ilmi.IntegrationTests;

/// <summary>Proves the API host composes and starts. Real endpoint tests arrive with the modules.</summary>
public class HostSmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void Host_Starts() =>
        Assert.NotNull(factory.CreateClient());
}
