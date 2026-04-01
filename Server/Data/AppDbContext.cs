namespace NCATAIBlazorFrontendTest.Server.Data
{
    using Microsoft.EntityFrameworkCore;
    using NCATAIBlazorFrontendTest.Shared;

    public class AppDbContext:DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
    }
}
