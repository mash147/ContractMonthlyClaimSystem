using ContractMonthlyClaimSystem.Models;
using System.ComponentModel.DataAnnotations;

namespace ContractMonthlyClaimSystem.Models.ViewModels
{
    public class DashboardStatistics
    {
        public decimal TotalApprovedAmount { get; set; }
        public double AverageProcessingTime { get; set; }
        public decimal ApprovalRate { get; set; }
        public string TopDepartment { get; set; }
        public List<ClaimTrend> ClaimsTrend { get; set; }
    }

    public class ClaimTrend
    {
        public string Period { get; set; }
        public int Count { get; set; }
    }

    public class ManagerStatistics
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int TotalClaims { get; set; }
        public int ApprovedClaims { get; set; }
        public int PendingClaims { get; set; }
        public int RejectedClaims { get; set; }
        public decimal TotalAmount { get; set; }
        public List<DepartmentStat> DepartmentStats { get; set; }
        public List<LecturerStat> LecturerStats { get; set; }
        public List<MonthlyBreakdown> MonthlyBreakdown { get; set; }
    }

    public class DepartmentStat
    {
        public string Department { get; set; }
        public int TotalClaims { get; set; }
        public int ApprovedClaims { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal AverageAmount { get; set; }
        public decimal ApprovalRate => TotalClaims > 0 ? (decimal)ApprovedClaims / TotalClaims * 100 : 0;
    }

    public class LecturerStat
    {
        public string LecturerName { get; set; }
        public string Department { get; set; }
        public int TotalClaims { get; set; }
        public int ApprovedClaims { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal ApprovalRate => TotalClaims > 0 ? (decimal)ApprovedClaims / TotalClaims * 100 : 0;
    }

    public class MonthlyBreakdown
    {
        public string Month { get; set; }
        public int Submitted { get; set; }
        public int Approved { get; set; }
        public decimal Amount { get; set; }
        public decimal ApprovalRate => Submitted > 0 ? (decimal)Approved / Submitted * 100 : 0;
    }

    public class ManagerReportViewModel
    {
        public string ReportType { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime GeneratedDate { get; set; }
        public List<Claim> Claims { get; set; }
        public ManagerStatistics Statistics { get; set; }
        public decimal TotalAmount { get; set; }
        public int TotalClaims { get; set; }
        public int ApprovedClaims { get; set; }
        public int PendingClaims { get; set; }
        public int RejectedClaims { get; set; }
    }

    public class ExportReportViewModel
    {
        public string ReportType { get; set; }

        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }
    }
}