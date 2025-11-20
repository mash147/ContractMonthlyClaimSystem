using ContractMonthlyClaimSystem.Models;
using ContractMonthlyClaimSystem.Models.ViewModels;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ContractMonthlyClaimSystem.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Lecturer> Lecturers { get; set; }
        public DbSet<ProgrammeCoordinator> ProgrammeCoordinators { get; set; }
        public DbSet<AcademicManager> AcademicManagers { get; set; }
        public DbSet<Claim> Claims { get; set; }
        public DbSet<SupportingDocument> SupportingDocuments { get; set; }
        public DbSet<AuditTrail> AuditTrails { get; set; }

        // Add these new DbSets
        public DbSet<PaymentBatch> PaymentBatches { get; set; }
        public DbSet<ScheduledReport> ScheduledReports { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure relationships
            modelBuilder.Entity<Lecturer>()
                .HasOne(l => l.User)
                .WithMany()
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProgrammeCoordinator>()
                .HasOne(pc => pc.User)
                .WithMany()
                .HasForeignKey(pc => pc.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AcademicManager>()
                .HasOne(am => am.User)
                .WithMany()
                .HasForeignKey(am => am.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Claim>()
                .HasOne(c => c.Lecturer)
                .WithMany(l => l.Claims)
                .HasForeignKey(c => c.LecturerID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SupportingDocument>()
                .HasOne(sd => sd.Claim)
                .WithMany(c => c.SupportingDocuments)
                .HasForeignKey(sd => sd.ClaimID);

            modelBuilder.Entity<AuditTrail>()
                .HasOne(at => at.Claim)
                .WithMany(c => c.AuditTrails)
                .HasForeignKey(at => at.ClaimID);

            modelBuilder.Entity<AuditTrail>()
                .HasOne(at => at.User)
                .WithMany()
                .HasForeignKey(at => at.UserId);

            // Configure PaymentBatch relationships
            modelBuilder.Entity<PaymentBatch>()
                .HasMany(pb => pb.Claims)
                .WithOne(c => c.PaymentBatch)
                .HasForeignKey(c => c.PaymentBatchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Claim>()
                .HasOne(c => c.PaymentBatch)
                .WithMany(pb => pb.Claims)
                .HasForeignKey(c => c.PaymentBatchId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure ScheduledReport (no relationships needed)
            modelBuilder.Entity<ScheduledReport>()
                .Property(sr => sr.ScheduleTime)
                .HasConversion(
                    v => v.Ticks,
                    v => TimeSpan.FromTicks(v)
                );

            modelBuilder.Entity<ScheduledReport>()
                .Property(sr => sr.ScheduleTime)
                .HasConversion(
                    v => v.Ticks,
                    v => TimeSpan.FromTicks(v)
                );
            // Seed some initial data for development
            SeedData(modelBuilder);
        }

        private void SeedData(ModelBuilder modelBuilder)
        {
            // Seed initial PaymentBatch (optional)
            modelBuilder.Entity<PaymentBatch>().HasData(
                new PaymentBatch
                {
                    BatchId = 1,
                    BatchNumber = "BATCH-INITIAL",
                    GeneratedDate = DateTime.Now.AddDays(-30),
                    TotalAmount = 0,
                    TotalClaims = 0,
                    GeneratedBy = "System"
                }
            );
        }
    }
}
