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
        public DbSet<PaymentBatch> PaymentBatches { get; set; }
        public DbSet<ScheduledReport> ScheduledReports { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Lecturer
            modelBuilder.Entity<Lecturer>(entity =>
            {
                entity.HasKey(e => e.LecturerID);

                entity.Property(e => e.Name)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Department)
                    .HasMaxLength(100);

                entity.Property(e => e.HourlyRate)
                    .HasColumnType("decimal(18,2)");

                entity.HasOne(l => l.User)
                    .WithMany()
                    .HasForeignKey(l => l.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure ProgrammeCoordinator
            modelBuilder.Entity<ProgrammeCoordinator>(entity =>
            {
                entity.HasKey(e => e.CoordinatorID);

                entity.HasOne(pc => pc.User)
                    .WithMany()
                    .HasForeignKey(pc => pc.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure AcademicManager
            modelBuilder.Entity<AcademicManager>(entity =>
            {
                entity.HasKey(e => e.ManagerID);

                entity.HasOne(am => am.User)
                    .WithMany()
                    .HasForeignKey(am => am.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure Claim - THIS IS THE MAIN FIX
            modelBuilder.Entity<Claim>(entity =>
            {
                entity.HasKey(e => e.ClaimID);

                entity.Property(e => e.HoursWorked)
                    .IsRequired()
                    .HasColumnType("decimal(18,2)");

                entity.Property(e => e.TotalHours)
                    .HasColumnType("decimal(18,2)")
                    .HasDefaultValue(0m);

                entity.Property(e => e.Amount)
                    .IsRequired()
                    .HasColumnType("decimal(18,2)");

                entity.Property(e => e.Status)
                    .IsRequired()
                    .HasMaxLength(20)
                    .HasDefaultValue("Pending");

                entity.Property(e => e.SubmissionDate)
                    .IsRequired();

                entity.Property(e => e.AdditionalNotes)
                    .HasMaxLength(500);

                entity.Property(e => e.IsPaid)
                    .HasDefaultValue(false);

                // Relationships
                entity.HasOne(c => c.Lecturer)
                    .WithMany(l => l.Claims)
                    .HasForeignKey(c => c.LecturerID)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(c => c.PaymentBatch)
                    .WithMany(pb => pb.Claims)
                    .HasForeignKey(c => c.PaymentBatchId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Configure SupportingDocument
            modelBuilder.Entity<SupportingDocument>(entity =>
            {
                entity.HasKey(e => e.DocumentID);

                entity.Property(e => e.FilePath)
                    .IsRequired()
                    .HasMaxLength(255);

                entity.Property(e => e.FileName)
                    .IsRequired()
                    .HasMaxLength(255);

                entity.Property(e => e.UploadedDate)
                    .IsRequired();

                entity.HasOne(sd => sd.Claim)
                    .WithMany(c => c.SupportingDocuments)
                    .HasForeignKey(sd => sd.ClaimID)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure AuditTrail
            modelBuilder.Entity<AuditTrail>(entity =>
            {
                entity.HasKey(e => e.AuditTrailID);

                entity.Property(e => e.Action)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Description)
                    .HasMaxLength(500);

                entity.Property(e => e.ActionDate)
                    .IsRequired();

                entity.Property(e => e.Timestamp)
                    .IsRequired();

                entity.Property(e => e.ActionBy)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.OldValues)
                    .HasMaxLength(4000);

                entity.Property(e => e.NewValues)
                    .HasMaxLength(4000);

                entity.Property(e => e.IPAddress)
                    .HasMaxLength(45);

                entity.HasOne(at => at.Claim)
                    .WithMany(c => c.AuditTrails)
                    .HasForeignKey(at => at.ClaimID)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(at => at.User)
                    .WithMany()
                    .HasForeignKey(at => at.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Configure PaymentBatch
            modelBuilder.Entity<PaymentBatch>(entity =>
            {
                entity.HasKey(e => e.BatchId);

                entity.Property(e => e.BatchNumber)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.GeneratedDate)
                    .IsRequired();

                entity.Property(e => e.TotalAmount)
                    .IsRequired()
                    .HasColumnType("decimal(18,2)");

                entity.Property(e => e.GeneratedBy)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.HasMany(pb => pb.Claims)
                    .WithOne(c => c.PaymentBatch)
                    .HasForeignKey(c => c.PaymentBatchId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Configure ScheduledReport
            modelBuilder.Entity<ScheduledReport>(entity =>
            {
                entity.HasKey(e => e.ScheduleId);

                entity.Property(e => e.ReportName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.Frequency)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(e => e.ScheduleTime)
                    .IsRequired()
                    .HasConversion(
                        v => v.Ticks,
                        v => TimeSpan.FromTicks(v)
                    );

                entity.Property(e => e.RecipientEmail)
                    .HasMaxLength(255);

                entity.Property(e => e.IsActive)
                    .HasDefaultValue(true);

                entity.Property(e => e.NextRunDate)
                    .IsRequired();

                entity.Property(e => e.CreatedDate)
                    .IsRequired();
            });

            // Remove duplicate ScheduledReport configuration
            // Remove this duplicate block:
            // modelBuilder.Entity<ScheduledReport>()
            //     .Property(sr => sr.ScheduleTime)
            //     .HasConversion(
            //         v => v.Ticks,
            //         v => TimeSpan.FromTicks(v)
            //     );

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
                    GeneratedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    TotalAmount = 0,
                    TotalClaims = 0,
                    GeneratedBy = "System"
                }
            );
        }
    }
}