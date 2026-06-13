namespace NCATAIBlazorFrontendTest.Client
{
    using Microsoft.AspNetCore.Components.Authorization;
    using System.Security.Claims;
    using System.Text.Json;

    public class MockAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly LocalStorageService _localStorage;
        private const string AuthKey = "mockAuthUser";

        private static readonly AuthenticationState Anonymous =
            new(new ClaimsPrincipal(new ClaimsIdentity()));

        public MockAuthenticationStateProvider(LocalStorageService localStorage)
        {
            _localStorage = localStorage;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                var json = await _localStorage.GetItemAsync(AuthKey);
                if (string.IsNullOrWhiteSpace(json))
                    return Anonymous;

                var data = JsonSerializer.Deserialize<MockAuthData>(json);
                if (data is null)
                    return Anonymous;

                return BuildState(data.UserId, data.Username, data.Role);
            }
            catch
            {
                return Anonymous;
            }
        }

        public async Task Login(string userId, string username, string role)
        {
            var data = new MockAuthData { UserId = userId, Username = username, Role = role };
            await _localStorage.SetItemAsync(AuthKey, JsonSerializer.Serialize(data));
            NotifyAuthenticationStateChanged(Task.FromResult(BuildState(userId, username, role)));
        }

        public async Task Logout()
        {
            await _localStorage.RemoveItemAsync(AuthKey);
            NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
        }

        private static AuthenticationState BuildState(string userId, string username, string role)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, "mock");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }

        private sealed record MockAuthData
        {
            public string UserId { get; init; } = "";
            public string Username { get; init; } = "";
            public string Role { get; init; } = "";
        }
    }
}
