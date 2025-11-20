using System;
using System.Collections.Generic;
using System.Linq;

namespace ContractMonthlyClaimSystem.Models
{
    public class CoordinatorReportViewModel
    {
        public string ReportType { get; set; } = "Monthly";
        public DateTime StartDate { get; set; } = DateTime.Now.AddDays(-30);
        public DateTime EndDate { get; set; } = DateTime.Now;
        public string Department { get; set; } = "All";
        public List<Claim> Claims { get; set; } = new List<Claim>();
        public decimal TotalAmount { get; set; }
        public int TotalClaims { get; set; }
        public int ApprovedClaims { get; set; }
        public int PendingClaims { get; set; }
        public int RejectedClaims { get; set; }
        public int UnderReviewClaims { get; set; }
        public double AverageProcessingTime { get; set; }

        // Calculated properties
        public decimal ApprovalRate => TotalClaims > 0 ? (decimal)ApprovedClaims / TotalClaims * 100 : 0;
        public decimal AverageClaimAmount => ApprovedClaims > 0 ? TotalAmount / ApprovedClaims : 0;
    }
}

