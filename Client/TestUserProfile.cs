namespace NCATAIBlazorFrontendTest.Client
{
    public class TestUserProfile
    {
        public string UserId { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Role { get; set; } = "";
        public List<string> PlayedSimIds { get; set; } = new();
    }
}
