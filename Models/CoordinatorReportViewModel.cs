using System;
using System.Collections.Generic;
using System.Linq;

namespace ContractMonthlyClaimSystem.Models
{
    public class CoordinatorReportViewModel
    {
        public string ReportType { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Department { get; set; }
        public List<Claim> Claims { get; set; }
        public decimal TotalAmount { get; set; }
        public int TotalClaims { get; set; }
        public int ApprovedClaims { get; set; }
        public int PendingClaims { get; set; }
        public int RejectedClaims { get; set; }
        public int UnderReviewClaims { get; set; }
        public double AverageProcessingTime { get; set; }
        public decimal ApprovalRate { get; set; } // Changed from get-only to get/set
        public decimal AverageClaimAmount { get; set; } // Changed from get-only to get/set
    }
}

