using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NCATAIBlazorFrontendTest.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<AuthenticationStateProvider, MockAuthenticationStateProvider>();
builder.Services.AddAuthorizationCore();

builder.Services.AddSingleton(new List<TestUserProfile>
{
    new TestUserProfile
    {
        UserId = "recursor-admin-01",
        Username = "recursor-admin-01",
        Password = "password123",
        Role = "Admin",
        PlayedSimIds = new List<string> { "Vials-and-Containers", "IV-Bags", "Syringes-and-Needles" }
    },
    new TestUserProfile
    {
        UserId = "medical-demo-user-001",
        Username = "medical-demo-user-001",
        Password = "password123",
        Role = "User",
        PlayedSimIds = new List<string> { "Vials-and-Containers" }
    },
    new TestUserProfile
    {
        UserId = "medical-demo-user-002",
        Username = "medical-demo-user-002",
        Password = "password123",
        Role = "User",
        PlayedSimIds = new List<string> { "Vials-and-Containers", "IV-Bags" }
    }
});

await builder.Build().RunAsync();
