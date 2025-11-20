
using System.ComponentModel.DataAnnotations;

namespace ContractMonthlyClaimSystem.Models.ViewModels
{
    public class EditLecturerViewModel
    {
        public int LecturerID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [StringLength(100)]
        public string Department { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal HourlyRate { get; set; }

        [Required]
        [StringLength(50)]
        public string EmployeeId { get; set; }

        [Phone]
        public string PhoneNumber { get; set; }
    }

    public class PaymentBatchViewModel
    {
        public string Period { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<Claim> Claims { get; set; }
        public List<int> SelectedClaimIds { get; set; }
        public decimal TotalAmount { get; set; }
        public int TotalClaims { get; set; }
    }

    public class InvoiceReportViewModel
    {
        public string ReportType { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<Claim> Claims { get; set; }
        public decimal TotalAmount { get; set; }
        public int TotalClaims { get; set; }
    }

    public class BulkUpdateViewModel
    {
        [Required]
        public string UpdateType { get; set; } // Fixed or Percentage

        [Required]
        [Range(0, double.MaxValue)]
        public decimal NewValue { get; set; }

        public string Department { get; set; } = "All";
    }

    public class HRAnalyticsViewModel
    {
        public int TotalLecturers { get; set; }
        public int ActiveLecturers { get; set; }
        public int TotalClaimsProcessed { get; set; }
        public decimal TotalPayments { get; set; }
        public List<DepartmentStat> DepartmentStats { get; set; }
        public List<MonthlyTrend> MonthlyTrends { get; set; }
    }

    public class MonthlyTrend
    {
        public string Month { get; set; }
        public int SubmittedClaims { get; set; }
        public int ApprovedClaims { get; set; }
        public decimal TotalAmount { get; set; }
    }

    // Additional model for Payment Batch
    public class PaymentBatch
    {
        [Key]
        public int BatchId { get; set; }

        [Required]
        [StringLength(50)]
        public string BatchNumber { get; set; }

        [Required]
        public DateTime GeneratedDate { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal TotalAmount { get; set; }

        [Required]
        public int TotalClaims { get; set; }

        [Required]
        [StringLength(100)]
        public string GeneratedBy { get; set; }

        // Navigation property
        public virtual ICollection<Claim> Claims { get; set; }
    }

    public class ScheduledReport
    {
        [Key]
        public int ScheduleId { get; set; }

        [Required]
        [StringLength(100)]
        public string ReportName { get; set; }

        [Required]
        [StringLength(20)]
        public string Frequency { get; set; } // Daily, Weekly, Monthly

        public TimeSpan ScheduleTime { get; set; }

        [EmailAddress]
        public string RecipientEmail { get; set; }

        public bool IsActive { get; set; }

        public DateTime NextRunDate { get; set; }

        public DateTime CreatedDate { get; set; }
    }




}
