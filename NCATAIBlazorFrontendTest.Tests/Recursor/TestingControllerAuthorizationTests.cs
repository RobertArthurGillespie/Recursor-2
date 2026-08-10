using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using NCATAIBlazorFrontendTest.Server.Controllers;
using NCATAIBlazorFrontendTest.Server.Recursor.Services;
using Xunit;

namespace NCATAIBlazorFrontendTest.Tests.Recursor;

/// <summary>
/// Corrective-pass Stage 2/8 tests: TestingController must require RecursorModelAdmin
/// authorization and must refuse to run outside Development, before it ever touches
/// Dropbox/storage configuration or credentials. SimulationStateController must reject
/// unsafe userId/simId identifiers (path-traversal / blob-path injection) before ever
/// calling into BlobStateService.
/// </summary>
public class TestingControllerAuthorizationTests
{
    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static TestingController BuildController(string environmentName)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        return new TestingController(
            NullLogger<TestingController>.Instance,
            config,
            new FakeWebHostEnvironment { EnvironmentName = environmentName });
    }

    [Fact]
    public void TestingController_HasRecursorModelAdminAuthorizeAttribute()
    {
        var authorizeAttribute = typeof(TestingController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttribute);
        Assert.Equal("RecursorModelAdmin", authorizeAttribute!.Policy);
    }

    [Fact]
    public async Task TestVideoConversion_ReturnsNotFound_OutsideDevelopment()
    {
        var controller = BuildController("Production");

        var result = await controller.TestVideoConversion();

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task TestingListDropbox_ReturnsNotFound_OutsideDevelopment()
    {
        var controller = BuildController("Production");

        var result = await controller.TestingListDropbox();

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task TestVideoConversion_InDevelopment_FailsClearlyWithoutLeakingCredentials_WhenDropboxNotConfigured()
    {
        var controller = BuildController("Development");

        // No Dropbox/storage config is present, so this must fail clearly (not silently, and
        // never by returning a token/secret) rather than proceeding with any fallback secret.
        var result = await controller.TestVideoConversion();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("Missing required configuration", content.Content);
        Assert.DoesNotContain("access token is:", content.Content);
    }
}

public class SimulationStateControllerValidationTests
{
    private static SimulationStateController BuildController() => new(null!);

    [Theory]
    [InlineData(null, "sim-1")]
    [InlineData("", "sim-1")]
    [InlineData("../../etc/passwd", "sim-1")]
    [InlineData("user/1", "sim-1")]
    [InlineData("user\\1", "sim-1")]
    public async Task GetState_RejectsUnsafeUserId_BeforeTouchingBlobService(string? userId, string simId)
    {
        var controller = BuildController();

        var result = await controller.GetState(userId!, simId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("user-1", "../secrets")]
    [InlineData("user-1", "")]
    public async Task GetState_RejectsUnsafeSimId_BeforeTouchingBlobService(string userId, string? simId)
    {
        var controller = BuildController();

        var result = await controller.GetState(userId, simId!);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SaveState_RejectsUnsafeUserId_BeforeTouchingBlobService()
    {
        var controller = BuildController();
        using var doc = System.Text.Json.JsonDocument.Parse("{}");

        var result = await controller.SaveState("../escape", "sim-1", doc.RootElement);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
