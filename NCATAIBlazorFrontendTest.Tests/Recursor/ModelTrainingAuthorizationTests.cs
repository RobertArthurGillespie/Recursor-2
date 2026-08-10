using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using NCATAIBlazorFrontendTest.Server.Configuration;
using NCATAIBlazorFrontendTest.Server.Controllers;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Corrective-pass Stage 3 tests: every Recursor model-training endpoint must require the
/// RecursorModelAdmin authorization policy, and the training-enabled/environment gate must
/// behave correctly. These are pure unit / policy-evaluation tests — no ASP.NET Core hosting
/// infrastructure is referenced by this test project, so the authorization policy is built and
/// evaluated directly via IAuthorizationService, and the environment/config gate is exercised
/// by calling the (internal, InternalsVisibleTo-exposed) RecursorController.CheckModelTrainingEnabled.
/// </summary>
public class ModelTrainingAuthorizationTests
{
    private static readonly string[] TrainingActionNames =
    {
        nameof(RecursorController.TrainTemporalRisk),
        nameof(RecursorController.TrainTemporalElevatedRisk),
        nameof(RecursorController.TrainTemporalBehaviorState),
    };

    // ── Authorization metadata ──────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(TrainingActionNamesData))]
    public void TrainingEndpoint_HasRecursorModelAdminAuthorizeAttribute(string actionName)
    {
        var method = typeof(RecursorController).GetMethod(actionName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);

        var authorizeAttribute = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttribute);
        Assert.Equal("RecursorModelAdmin", authorizeAttribute!.Policy);
    }

    public static IEnumerable<object[]> TrainingActionNamesData() =>
        TrainingActionNames.Select(name => new object[] { name });

    // ── Policy evaluation (mirrors Program.cs AddPolicy("RecursorModelAdmin", ...)) ─────────

    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("RecursorModelAdmin", policy => policy.RequireRole("Admin"));
        });
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    [Fact]
    public async Task RecursorModelAdminPolicy_RejectsUnauthenticatedUser()
    {
        var authService = BuildAuthorizationService();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity()); // no authentication type => not authenticated

        var result = await authService.AuthorizeAsync(anonymous, null, "RecursorModelAdmin");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RecursorModelAdminPolicy_RejectsAuthenticatedNonAdmin()
    {
        var authService = BuildAuthorizationService();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "User") }, authenticationType: "TestAuth");
        var nonAdmin = new ClaimsPrincipal(identity);

        var result = await authService.AuthorizeAsync(nonAdmin, null, "RecursorModelAdmin");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RecursorModelAdminPolicy_AllowsAuthenticatedAdmin()
    {
        var authService = BuildAuthorizationService();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }, authenticationType: "TestAuth");
        var admin = new ClaimsPrincipal(identity);

        var result = await authService.AuthorizeAsync(admin, null, "RecursorModelAdmin");

        Assert.True(result.Succeeded);
    }

    // ── EnableModelTraining / allowed-environment gate ──────────────────────

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static RecursorController BuildController(RecursorPoliciesOptions policies, string environmentName)
    {
        return new RecursorController(
            sessionService: null!,
            policyRecommendationService: null!,
            sessionRepository: null!,
            sequenceFeatureExtractor: null!,
            temporalEmbedding: null!,
            temporalRiskTraining: null!,
            temporalElevatedRiskTraining: null!,
            temporalBehaviorStateTraining: null!,
            simContractValidator: null!,
            config: null!,
            policiesOptions: Options.Create(policies),
            env: new FakeWebHostEnvironment { EnvironmentName = environmentName });
    }

    [Fact]
    public void CheckModelTrainingEnabled_ReturnsForbidden_WhenDisabledByConfig()
    {
        var policies = new RecursorPoliciesOptions
        {
            EnableModelTraining = false,
            ModelTrainingAllowedEnvironments = new[] { "Development" },
        };
        var controller = BuildController(policies, "Development");

        var result = controller.CheckModelTrainingEnabled();

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public void CheckModelTrainingEnabled_ReturnsForbidden_WhenEnvironmentNotAllowed()
    {
        var policies = new RecursorPoliciesOptions
        {
            EnableModelTraining = true,
            ModelTrainingAllowedEnvironments = new[] { "Development" },
        };
        var controller = BuildController(policies, "Production");

        var result = controller.CheckModelTrainingEnabled();

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Fact]
    public void CheckModelTrainingEnabled_ReturnsNull_WhenEnabledAndEnvironmentAllowed()
    {
        var policies = new RecursorPoliciesOptions
        {
            EnableModelTraining = true,
            ModelTrainingAllowedEnvironments = new[] { "Development", "Staging" },
        };
        var controller = BuildController(policies, "Staging");

        var result = controller.CheckModelTrainingEnabled();

        Assert.Null(result);
    }

    [Fact]
    public void RecursorPoliciesOptions_ModelTrainingDefaults_AreSecureByDefault()
    {
        var defaults = new RecursorPoliciesOptions();

        Assert.True(defaults.EnableModelTraining);
        Assert.Equal(new[] { "Development" }, defaults.ModelTrainingAllowedEnvironments);
    }
}
