using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Ilmi.Modules.UsersIdentity.Tests;

/// <summary>Drives the real API host (controllers, session authentication, problem details) over HTTP against a real database.</summary>
[Collection("sql")]
public class HttpFlowTests(SqlServerFixture db)
{
    private WebApplicationFactory<Program> Factory(RecordingEmailSender? email = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:UsersIdentity", db.ConnectionString);
            if (email is not null) b.ConfigureTestServices(s => s.AddSingleton<IIdentityEmailSender>(email));
        });

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Disability_type_lookup_is_public_and_returns_the_four_SRS_values()
    {
        await using var factory = Factory();
        var response = await factory.CreateClient().GetAsync("/api/lookups/disability-types");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await Json(response)).EnumerateArray().ToList();
        Assert.Equal(["Unable to Walk", "Unable to Walk Long Distances", "Visual Impairment / Blindness", "Hearing Loss / Deafness"],
            items.Select(i => i.GetProperty("nameEn").GetString()!).ToArray());
        Assert.Equal("عدم القدرة على المشي", items[0].GetProperty("nameAr").GetString());
    }

    [Fact]
    public async Task Personal_endpoints_require_a_session()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "forged-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Register_login_with_email_otp_use_the_session_and_log_out()
    {
        var email = new RecordingEmailSender();
        await using var factory = Factory(email);
        var client = factory.CreateClient();
        var address = TestApp.NewEmail();

        var request = await client.PostAsJsonAsync("/api/auth/register/request", new
        {
            firstName = "Sara", lastName = "Alharbi", email = address, mobileCountryCode = "+966", mobileNationalNumber = TestApp.NewMobile(),
            dateOfBirth = "1990-05-20", gender = "Female", receiveNewsletter = true, notifyIlmiSpecialNeeds = false,
            disabilityTypeIds = new[] { 1, 3 }, language = "ar", acceptedTerms = true
        });
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);

        var verified = await client.PostAsJsonAsync("/api/auth/register/verify", new { email = address, code = email.LastOtp });
        Assert.Equal(HttpStatusCode.Created, verified.StatusCode);
        var password = email.LastPassword!;

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = address, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var otp = await client.PostAsJsonAsync("/api/auth/login/verify-otp", new { email = address, code = email.LastOtp, clientType = "Web" });
        Assert.Equal(HttpStatusCode.OK, otp.StatusCode);
        var token = (await Json(otp)).GetProperty("token").GetString()!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await Json(await client.GetAsync("/api/me"));
        Assert.Equal("Adult", me.GetProperty("userType").GetString());
        Assert.Equal("External", me.GetProperty("userKind").GetString());
        Assert.Equal("ADULT", me.GetProperty("rules")[0].GetProperty("code").GetString());

        var settings = await Json(await client.GetAsync("/api/me/settings"));
        Assert.Equal("ar", settings.GetProperty("language").GetString());
        Assert.Equal(2, (await Json(await client.GetAsync("/api/me/accessibility-needs"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/me/settings", new { language = "en", emailNotificationsEnabled = false })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);        // revoked immediately
    }

    [Fact]
    public async Task Business_failures_are_problem_details_with_a_stable_code()
    {
        await using var factory = Factory(new RecordingEmailSender());
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = TestApp.NewEmail(), password = "not-registered-123" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.invalid_credentials", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_missing_email_integration_is_reported_as_503_never_silently_accepted()
    {
        await using var factory = Factory();      // the default adapter is the "unconfigured" one
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register/request", new
        {
            firstName = "Sara", lastName = "Alharbi", email = TestApp.NewEmail(), mobileCountryCode = "+966", mobileNationalNumber = TestApp.NewMobile(),
            dateOfBirth = "1990-05-20", language = "en", acceptedTerms = true
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("integration.not_configured", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Social_sign_in_is_refused_until_a_provider_verifier_is_configured()
    {
        await using var factory = Factory();
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login/external", new { provider = "Google", idToken = "x", clientType = "Web" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task The_OpenAPI_document_lists_the_module_endpoints()
    {
        await using var factory = Factory();
        var response = await factory.CreateClient().GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = (await Json(response)).GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/auth/register/request", paths);
        Assert.Contains("/api/auth/quick-login/challenge", paths);
        Assert.Contains("/api/me/settings", paths);
    }
}
