using Microsoft.EntityFrameworkCore;

namespace NextHorizon.Models
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<FAQ> FAQs { get; set; }
    }
}