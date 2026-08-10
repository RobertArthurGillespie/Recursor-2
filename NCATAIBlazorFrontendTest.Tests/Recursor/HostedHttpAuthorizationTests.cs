using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Recursor.ML;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Stage 3 (corrective pass): exercises Recursor model-training and TestingController
/// authorization through the REAL hosted ASP.NET Core pipeline (WebApplicationFactory), unlike
/// ModelTrainingAuthorizationTests, which verifies authorization metadata and policy evaluation
/// directly (via reflection and IAuthorizationService) without ever routing a real HTTP request
/// through the middleware pipeline. That unit-level coverage could not have caught Finding A —
/// UseAuthorization()/UseAuthentication() running ahead of UseRouting() in Program.cs, which
/// means the authorization middleware never actually saw the routed endpoint's [Authorize]
/// metadata (Microsoft.AspNetCore.Http.Endpoint is null until routing has run) and so never truly
/// enforced per-endpoint policies. These tests fail if that ordering regresses.
///
/// A custom "Test" authentication scheme (TestAuthHandler) stands in for the real JWT bearer
/// scheme so unauthenticated/non-admin/admin outcomes can be driven via request headers instead
/// of minting real JWTs.
///
/// Required startup secrets (Jwt:Key, ConnectionStrings:DefaultConnection) are supplied via
/// process environment variables set in this class's static constructor. This has to happen via
/// environment variables specifically (not WebApplicationFactory's ConfigureWebHost/
/// ConfigureAppConfiguration) because Program.cs's RequireSecret checks run synchronously against
/// builder.Configuration BEFORE builder.Build() is called, and WebApplicationFactory's
/// customizations for WebApplicationBuilder-based (top-level Program.cs) apps are only spliced
/// into the builder at Build()-time — too late to affect code that already ran before it.
/// Adx:ClusterUri is also blanked via an environment variable so no ADX client is ever
/// constructed and no live Azure/ADX call is possible anywhere in this test class.
/// </summary>
public class HostedHttpAuthorizationTests : IClassFixture<HostedHttpAuthorizationTests.Factory>
{
    public const string TestAuthScheme = "Test";
    public const string AuthenticatedHeader = "X-Test-Authenticated";
    public const string RoleHeader = "X-Test-Role";

    // An environment name deliberately absent from the default
    // Recursor:Policies:ModelTrainingAllowedEnvironments (["Development"]) — used to prove the
    // controller's own environment gate still applies after authorization succeeds.
    private const string DisallowedTrainingEnvironment = "HostedAuthTest";

    static HostedHttpAuthorizationTests()
    {
        Environment.SetEnvironmentVariable("Jwt__Key", "hosted-auth-test-signing-key-0123456789abcdef");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",
            "Server=(local);Database=RecursorHostedAuthTests;Integrated Security=true;TrustServerCertificate=true;");
        // No live ADX/Azure calls from this test class, ever.
        Environment.SetEnvironmentVariable("Adx__ClusterUri", "");
        Environment.SetEnvironmentVariable("Adx__IngestUri", "");
    }

    private readonly Factory _factory;

    public HostedHttpAuthorizationTests(Factory factory)
    {
        _factory = factory;
    }

    // ── Custom "Test" authentication scheme ─────────────────────────────────

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(AuthenticatedHeader, out var authenticated) ||
                !string.Equals(authenticated, "true", StringComparison.OrdinalIgnoreCase))
            {
                // No successful authentication result — [Authorize] endpoints challenge (401).
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(TestAuthScheme);
            if (Request.Headers.TryGetValue(RoleHeader, out var role) && !string.IsNullOrEmpty(role))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, role!));
            }

            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), TestAuthScheme);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class FakeTemporalBehaviorStateModelTrainingService : ITemporalBehaviorStateModelTrainingService
    {
        public bool WasCalled { get; private set; }

        public Task<TemporalBehaviorStateTrainingReport> TrainAsync(string? modelVersion = null)
        {
            WasCalled = true;
            return Task.FromResult(new TemporalBehaviorStateTrainingReport
            {
                ModelVersion = modelVersion ?? "fake-version",
                GeneratedAtUtc = DateTime.UtcNow,
                Published = false,
                PublishError = "Fake training service — no real ADX/model-training work performed.",
            });
        }
    }

    // Replaces Program.cs's real JWT bearer default scheme with the header-driven TestAuthHandler
    // above, so tests can drive unauthenticated/non-admin/admin outcomes without real JWTs. This
    // override applies to every host built from this factory, including WithWebHostBuilder(...)
    // variants used per-test below.
    public class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAuthScheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthScheme, _ => { });
            });
        }
    }

    // ── train-temporal-behavior-state ───────────────────────────────────────

    [Fact]
    public async Task TrainEndpoint_Unauthenticated_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/recursor/train-temporal-behavior-state", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TrainEndpoint_AuthenticatedNonAdmin_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthenticatedHeader, "true");
        client.DefaultRequestHeaders.Add(RoleHeader, "User");

        var response = await client.PostAsync("/api/recursor/train-temporal-behavior-state", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // Distinguishes this authorization-middleware 403 from the controller's own
        // CheckModelTrainingEnabled gate 403 exercised below — that gate never runs here because
        // authorization rejected the request before the controller was ever reached.
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Model training is", body);
    }

    [Fact]
    public async Task TrainEndpoint_AdminInDisallowedEnvironment_PassesAuthorizationButControllerGateForbids()
    {
        using var scopedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(DisallowedTrainingEnvironment);
        });
        var client = scopedFactory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthenticatedHeader, "true");
        client.DefaultRequestHeaders.Add(RoleHeader, "Admin");

        var response = await client.PostAsync("/api/recursor/train-temporal-behavior-state", null);

        // Proves authorization let the request through to the controller (not a 401/403 from
        // auth) — this 403 comes from RecursorController.CheckModelTrainingEnabled's environment
        // gate, which only runs after authorization has already succeeded.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("environment", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TrainEndpoint_AdminInAllowedEnvironment_ReachesControllerAndInvokesTrainingService()
    {
        var fake = new FakeTemporalBehaviorStateModelTrainingService();
        using var scopedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development"); // matches default ModelTrainingAllowedEnvironments
            builder.ConfigureTestServices(services =>
            {
                // Avoids any real ADX query / ML.NET training / model-file I/O — this test only
                // proves the HTTP pipeline (routing -> auth -> authorization -> controller ->
                // environment gate -> downstream service) reaches the controller and invokes it.
                services.AddSingleton<ITemporalBehaviorStateModelTrainingService>(fake);
            });
        });
        var client = scopedFactory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthenticatedHeader, "true");
        client.DefaultRequestHeaders.Add(RoleHeader, "Admin");

        var response = await client.PostAsync("/api/recursor/train-temporal-behavior-state", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(fake.WasCalled);
    }

    // ── TestingController ────────────────────────────────────────────────────

    [Fact]
    public async Task TestingController_Unauthenticated_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/Testing/TestVideoConversion");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TestingController_AuthenticatedNonAdmin_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthenticatedHeader, "true");
        client.DefaultRequestHeaders.Add(RoleHeader, "User");

        var response = await client.GetAsync("/Testing/TestVideoConversion");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TestingController_AdminOutsideDevelopment_AuthorizationPassesButActionRemainsGated()
    {
        using var scopedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(DisallowedTrainingEnvironment);
        });
        var client = scopedFactory.CreateClient();
        client.DefaultRequestHeaders.Add(AuthenticatedHeader, "true");
        client.DefaultRequestHeaders.Add(RoleHeader, "Admin");

        var response = await client.GetAsync("/Testing/TestVideoConversion");

        // Proves authorization passed (not 401/403 from auth) and TestVideoConversion's own
        // `if (!_env.IsDevelopment()) return NotFound();` gate still applies afterward — the
        // Dropbox/Blob-Storage-touching body of this developer-only action is never reached
        // outside Development regardless of who is authorized.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
