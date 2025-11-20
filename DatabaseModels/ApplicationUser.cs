using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ContractMonthlyClaimSystem.Models.ViewModels;
using Microsoft.AspNetCore.Identity;

namespace ContractMonthlyClaimSystem.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [StringLength(100)]
        public string FullName { get; set; }

        [Required]
        [StringLength(20)]
        public string Role { get; set; }

        public string Department { get; set; }
        public decimal? HourlyRate { get; set; }
        public string EmployeeId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class Lecturer
    {
        [Key]
        public int LecturerID { get; set; }

        [Required]
        public string UserId { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [StringLength(100)]
        public string Department { get; set; }

        [Range(0, double.MaxValue)]
        public decimal HourlyRate { get; set; }

        public ApplicationUser User { get; set; }
        public ICollection<Claim> Claims { get; set; }


    }

    public class ProgrammeCoordinator
    {
        [Key]
        public int CoordinatorID { get; set; }
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }
    }

    public class AcademicManager
    {
        [Key]
        public int ManagerID { get; set; }
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }
    }

    public class Claim
    {
        [Key]
        public int ClaimID { get; set; }

        [Required]
        public int LecturerID { get; set; }

        [ForeignKey("LecturerID")]
        public virtual Lecturer? Lecturer { get; set; }

        // Hours worked for this claim
        [Required]
        [Range(0, 200)]
        public decimal HoursWorked { get; set; }

        // Total hours (if used for calculated summaries)
        public decimal TotalHours { get; set; }

        // Amount to be paid for this claim
        [Required]
        [Range(0, double.MaxValue)]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Pending";  // Pending, Approved, Rejected

        [DataType(DataType.Date)]
        public DateTime SubmissionDate { get; set; }

        [StringLength(500)]
        public string? AdditionalNotes { get; set; }

        // Payment tracking
        public bool IsPaid { get; set; } = false;

        public DateTime? PaymentDate { get; set; }

        public int? PaymentBatchId { get; set; }

        [ForeignKey("PaymentBatchId")]
        public virtual PaymentBatch? PaymentBatch { get; set; }

        // Navigation properties
        public virtual ICollection<SupportingDocument>? SupportingDocuments { get; set; }
            = new List<SupportingDocument>();

        public virtual ICollection<AuditTrail> AuditTrails { get; set; }
            = new List<AuditTrail>();
    }

    public class SupportingDocument
    {
        [Key]
        public int DocumentID { get; set; }

        [Required]
        public int ClaimID { get; set; }

        [Required]
        [StringLength(255)]
        public string FilePath { get; set; }

        [Required]
        [StringLength(255)]
        public string FileName { get; set; }

        [DataType(DataType.Date)]
        public DateTime UploadedDate { get; set; }

        public Claim Claim { get; set; }
    }

  
   
        public class AuditTrail
        {
            [Key]
            public int AuditTrailID { get; set; }

            [Required]
            public int ClaimID { get; set; }

            [ForeignKey("ClaimID")]
            public virtual Claim Claim { get; set; }

            // Add User reference
            public string? UserId { get; set; }

            [ForeignKey("UserId")]
            public virtual ApplicationUser? User { get; set; }

            [Required]
            [StringLength(100)]
            public string Action { get; set; } = string.Empty;

            [StringLength(500)]
            public string Description { get; set; } = string.Empty;

            [Required]
            public DateTime ActionDate { get; set; }

            public DateTime Timestamp { get; set; }

            [Required]
            [StringLength(100)]
            public string ActionBy { get; set; } = string.Empty;

            // Additional properties you might want
            public string OldValues { get; set; } = string.Empty;
            public string NewValues { get; set; } = string.Empty;
            public string IPAddress { get; set; } = string.Empty;

            // Computed property for display
            [NotMapped]
            public string UserName => User?.UserName ?? ActionBy;
        }
    
}
