using Microsoft.EntityFrameworkCore;

namespace FeuerSoftware.MailAgent.Data
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<MailAgentSettingsEntity> MailAgentSettings { get; set; } = null!;
        public DbSet<EmailAccountEntity> EmailAccounts { get; set; } = null!;
        public DbSet<PatternSettingsEntity> PatternSettings { get; set; } = null!;
        public DbSet<AdditionalPatternEntity> AdditionalPatterns { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // PatternSettingsEntity → AdditionalPatternEntity (cascade delete)
            modelBuilder.Entity<PatternSettingsEntity>()
                .HasMany(p => p.AdditionalProperties)
                .WithOne()
                .HasForeignKey(ap => ap.PatternSettingsId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
