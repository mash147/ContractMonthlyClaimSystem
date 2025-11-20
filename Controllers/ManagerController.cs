using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ContractMonthlyClaimSystem.Models;
using ContractMonthlyClaimSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using ContractMonthlyClaimSystem.Data;
using ContractMonthlyClaimSystem.Models.ViewModels;

namespace ContractMonthlyClaimSystem.Controllers
{
    [Authorize(Roles = "Manager")]
    public class ManagerController : Controller
    {
        private readonly IClaimService _claimService;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ManagerController(
            IClaimService claimService,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _claimService = claimService;
            _context = context;
            _userManager = userManager;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        public async Task<IActionResult> Dashboard()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var pendingApproval = await GetClaimsPendingManagerApprovalAsync();

                ViewBag.UserName = user?.FullName ?? "Manager";
                ViewBag.PendingApprovalCount = pendingApproval.Count;
                ViewBag.TotalClaimsCount = await _context.Claims.CountAsync();
                ViewBag.TotalAmountThisMonth = await GetTotalAmountThisMonthAsync();
                ViewBag.TotalLecturers = await _context.Lecturers.CountAsync();

                // Statistics for dashboard
                ViewBag.Statistics = await GetDashboardStatisticsAsync();

                return View(pendingApproval);
            }
            catch (Exception ex)
            {
                // Log the exception
                TempData["ErrorMessage"] = "Error loading dashboard: " + ex.Message;
                return View(new List<Claim>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> ApproveClaims(string status = "Coordinator Approved")
        {
            try
            {
                var claims = await GetClaimsForManagerApprovalAsync(status);
                ViewBag.CurrentStatus = status;
                ViewBag.StatusList = new List<string>
                {
                    "Coordinator Approved",
                    "Pending",
                    "Approved",
                    "Rejected",
                    "Under Review"
                };

                return View(claims);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading claims: " + ex.Message;
                return View(new List<Claim>());
            }
        }

        [HttpPost]
        public async Task<IActionResult> FinalApproveClaim(int claimId)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Approved";
                    await _context.SaveChangesAsync();

                    await _claimService.LogAuditAsync(claimId, user.Id, "Claim finally approved by Manager");

                    TempData["SuccessMessage"] = "Claim finally approved!";
                }
                else
                {
                    TempData["ErrorMessage"] = "Claim not found.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error approving claim: " + ex.Message;
            }

            return RedirectToAction("ApproveClaims");
        }

        [HttpPost]
        public async Task<IActionResult> ManagerRejectClaim(int claimId, string reason)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Rejected";
                    claim.AdditionalNotes += $"\nManager Rejection: {reason}";
                    await _context.SaveChangesAsync();

                    await _claimService.LogAuditAsync(claimId, user.Id, $"Claim rejected by Manager: {reason}");

                    TempData["SuccessMessage"] = "Claim rejected successfully!";
                }
                else
                {
                    TempData["ErrorMessage"] = "Claim not found.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error rejecting claim: " + ex.Message;
            }

            return RedirectToAction("ApproveClaims");
        }

        [HttpGet]
        public async Task<IActionResult> AuditTrail(DateTime? startDate = null, DateTime? endDate = null, string actionFilter = "All")
        {
            try
            {
                // Set default date range to last 30 days if not provided
                if (!startDate.HasValue || !endDate.HasValue)
                {
                    endDate = DateTime.Now;
                    startDate = endDate.Value.AddDays(-30);
                }

                var auditQuery = _context.AuditTrails
                    .Include(a => a.User)
                    .Include(a => a.Claim)
                    .ThenInclude(c => c.Lecturer)
                    .Where(a => a.Timestamp >= startDate && a.Timestamp <= endDate);

                if (actionFilter != "All")
                {
                    auditQuery = auditQuery.Where(a => a.Action.Contains(actionFilter));
                }

                var auditTrails = await auditQuery
                    .OrderByDescending(a => a.Timestamp)
                    .ToListAsync();

                ViewBag.StartDate = startDate.Value;
                ViewBag.EndDate = endDate.Value;
                ViewBag.ActionFilter = actionFilter;
                ViewBag.ActionTypes = new List<string>
                {
                    "All",
                    "Submitted",
                    "Approved",
                    "Rejected",
                    "Uploaded",
                    "Verified"
                };

                return View(auditTrails);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading audit trail: " + ex.Message;
                return View(new List<AuditTrail>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> Statistics()
        {
            try
            {
                var statistics = await GetDetailedStatisticsAsync();
                return View(statistics);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading statistics: " + ex.Message;
                return View(new ManagerStatistics());
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportReports(string reportType = "Comprehensive", DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                // Set default date range to current month if not provided
                if (!startDate.HasValue || !endDate.HasValue)
                {
                    startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                    endDate = startDate.Value.AddMonths(1).AddDays(-1);
                }

                var reportData = await GenerateComprehensiveReportAsync(startDate.Value, endDate.Value, reportType);

                ViewBag.ReportTypes = new List<string>
                {
                    "Comprehensive",
                    "Financial",
                    "Claims Summary",
                    "Lecturer Performance",
                    "Department Summary"
                };
                ViewBag.StartDate = startDate.Value;
                ViewBag.EndDate = endDate.Value;
                ViewBag.ReportType = reportType;

                return View(reportData);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading export reports: " + ex.Message;
                return View(new ManagerReportViewModel());
            }
        }

        [HttpPost]
        public async Task<IActionResult> GenerateExport(ExportReportViewModel model)
        {
            return RedirectToAction("ExportReports", new
            {
                reportType = model.ReportType,
                startDate = model.StartDate,
                endDate = model.EndDate
            });
        }

        [HttpGet]
        public IActionResult DownloadReport(string reportType, DateTime startDate, DateTime endDate, string format = "PDF")
        {
            // For now, just redirect back to ExportReports
            // In a real application, you would generate PDF/Excel here
            TempData["InfoMessage"] = $"Export functionality for {reportType} report ({format}) would be implemented here.";
            return RedirectToAction("ExportReports", new
            {
                reportType = reportType,
                startDate = startDate,
                endDate = endDate
            });
        }

        [HttpGet]
        public async Task<IActionResult> ClaimAnalysis(int id)
        {
            try
            {
                var claim = await _context.Claims
                    .Include(c => c.Lecturer)
                    .Include(c => c.SupportingDocuments)
                    .Include(c => c.AuditTrails)
                    .ThenInclude(a => a.User)
                    .FirstOrDefaultAsync(c => c.ClaimID == id);

                if (claim == null)
                {
                    TempData["ErrorMessage"] = "Claim not found.";
                    return RedirectToAction("ApproveClaims");
                }

                // Get similar claims for comparison
                var similarClaims = await _context.Claims
                    .Include(c => c.Lecturer)
                    .Where(c => c.LecturerID == claim.LecturerID && c.ClaimID != id)
                    .OrderByDescending(c => c.SubmissionDate)
                    .Take(5)
                    .ToListAsync();

                ViewBag.SimilarClaims = similarClaims;

                return View(claim);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading claim analysis: " + ex.Message;
                return RedirectToAction("ApproveClaims");
            }
        }

        // Private helper methods
        private async Task<List<Claim>> GetClaimsPendingManagerApprovalAsync()
        {
            return await _context.Claims
                .Include(c => c.Lecturer)
                .ThenInclude(l => l.User)
                .Include(c => c.SupportingDocuments)
                .Where(c => c.Status == "Coordinator Approved" || c.Status == "Under Review")
                .OrderByDescending(c => c.SubmissionDate)
                .ToListAsync();
        }

        private async Task<List<Claim>> GetClaimsForManagerApprovalAsync(string status)
        {
            var query = _context.Claims
                .Include(c => c.Lecturer)
                .ThenInclude(l => l.User)
                .Include(c => c.SupportingDocuments)
                .Include(c => c.AuditTrails)
                .AsQueryable();

            if (status == "Coordinator Approved")
            {
                query = query.Where(c => c.Status == "Coordinator Approved");
            }
            else
            {
                query = query.Where(c => c.Status == status);
            }

            return await query
                .OrderByDescending(c => c.SubmissionDate)
                .ToListAsync();
        }

        private async Task<decimal> GetTotalAmountThisMonthAsync()
        {
            var startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var amount = await _context.Claims
                .Where(c => c.Status == "Approved" && c.SubmissionDate >= startDate && c.SubmissionDate <= endDate)
                .SumAsync(c => c.Amount);

            return amount;
        }

        private async Task<DashboardStatistics> GetDashboardStatisticsAsync()
        {
            var startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var claims = await _context.Claims
                .Where(c => c.SubmissionDate >= startDate.AddMonths(-6)) // Last 6 months
                .ToListAsync();

            var approvedClaims = claims.Where(c => c.Status == "Approved").ToList();

            return new DashboardStatistics
            {
                TotalApprovedAmount = approvedClaims.Sum(c => c.Amount),
                AverageProcessingTime = 2.5, // This would be calculated from audit trails
                ApprovalRate = claims.Any() ? (decimal)approvedClaims.Count / claims.Count * 100 : 0,
                TopDepartment = await GetTopDepartmentAsync(),
                ClaimsTrend = await GetClaimsTrendAsync()
            };
        }

        private async Task<string> GetTopDepartmentAsync()
        {
            var topDept = await _context.Claims
                .Include(c => c.Lecturer)
                .Where(c => c.Status == "Approved")
                .GroupBy(c => c.Lecturer.Department)
                .Select(g => new { Department = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .FirstOrDefaultAsync();

            return topDept?.Department ?? "N/A";
        }

        private async Task<List<ClaimTrend>> GetClaimsTrendAsync()
        {
            var trends = new List<ClaimTrend>();
            for (int i = 5; i >= 0; i--)
            {
                var month = DateTime.Now.AddMonths(-i);
                var startDate = new DateTime(month.Year, month.Month, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);

                var monthlyClaims = await _context.Claims
                    .CountAsync(c => c.SubmissionDate >= startDate && c.SubmissionDate <= endDate);

                trends.Add(new ClaimTrend
                {
                    Period = startDate.ToString("MMM yyyy"),
                    Count = monthlyClaims
                });
            }

            return trends;
        }

        private async Task<ManagerStatistics> GetDetailedStatisticsAsync()
        {
            var startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(-6);
            var endDate = DateTime.Now;

            var claims = await _context.Claims
                .Include(c => c.Lecturer)
                .Where(c => c.SubmissionDate >= startDate && c.SubmissionDate <= endDate)
                .ToListAsync();

            var departmentStats = await _context.Claims
                .Include(c => c.Lecturer)
                .Where(c => c.SubmissionDate >= startDate && c.SubmissionDate <= endDate)
                .GroupBy(c => c.Lecturer.Department)
                .Select(g => new DepartmentStat
                {
                    Department = g.Key ?? "Unknown",
                    TotalClaims = g.Count(),
                    ApprovedClaims = g.Count(c => c.Status == "Approved"),
                    TotalAmount = g.Where(c => c.Status == "Approved").Sum(c => c.Amount)
                })
                .OrderByDescending(d => d.TotalAmount)
                .ToListAsync();

            var lecturerStats = await _context.Claims
                .Include(c => c.Lecturer)
                .Where(c => c.SubmissionDate >= startDate && c.SubmissionDate <= endDate)
                .GroupBy(c => c.Lecturer.Name)
                .Select(g => new LecturerStat
                {
                    LecturerName = g.Key ?? "Unknown",
                    TotalClaims = g.Count(),
                    ApprovedClaims = g.Count(c => c.Status == "Approved"),
                    TotalAmount = g.Where(c => c.Status == "Approved").Sum(c => c.Amount),
                    Department = g.Any() && g.First().Lecturer != null ? g.First().Lecturer.Department : "Unknown"
                })
                .OrderByDescending(l => l.TotalAmount)
                .Take(10)
                .ToListAsync();

            return new ManagerStatistics
            {
                StartDate = startDate,
                EndDate = endDate,
                TotalClaims = claims.Count,
                ApprovedClaims = claims.Count(c => c.Status == "Approved"),
                PendingClaims = claims.Count(c => c.Status == "Pending" || c.Status == "Under Review"),
                RejectedClaims = claims.Count(c => c.Status == "Rejected"),
                TotalAmount = claims.Where(c => c.Status == "Approved").Sum(c => c.Amount),
                DepartmentStats = departmentStats,
                LecturerStats = lecturerStats,
                MonthlyBreakdown = await GetMonthlyBreakdownAsync(startDate, endDate)
            };
        }

        private async Task<List<MonthlyBreakdown>> GetMonthlyBreakdownAsync(DateTime startDate, DateTime endDate)
        {
            var breakdown = new List<MonthlyBreakdown>();
            var current = startDate;

            while (current <= endDate)
            {
                var monthEnd = new DateTime(current.Year, current.Month, 1).AddMonths(1).AddDays(-1);
                var monthlyClaims = await _context.Claims
                    .Where(c => c.SubmissionDate >= current && c.SubmissionDate <= monthEnd)
                    .ToListAsync();

                breakdown.Add(new MonthlyBreakdown
                {
                    Month = current.ToString("MMM yyyy"),
                    Submitted = monthlyClaims.Count,
                    Approved = monthlyClaims.Count(c => c.Status == "Approved"),
                    Amount = monthlyClaims.Where(c => c.Status == "Approved").Sum(c => c.Amount)
                });

                current = current.AddMonths(1);
            }

            return breakdown;
        }

        private async Task<ManagerReportViewModel> GenerateComprehensiveReportAsync(DateTime startDate, DateTime endDate, string reportType)
        {
            var claims = await _context.Claims
                .Include(c => c.Lecturer)
                .Include(c => c.AuditTrails)
                .Where(c => c.SubmissionDate >= startDate && c.SubmissionDate <= endDate)
                .ToListAsync();

            var statistics = await GetDetailedStatisticsAsync();

            return new ManagerReportViewModel
            {
                ReportType = reportType,
                StartDate = startDate,
                EndDate = endDate,
                GeneratedDate = DateTime.Now,
                Claims = claims,
                Statistics = statistics,
                TotalAmount = claims.Where(c => c.Status == "Approved").Sum(c => c.Amount),
                TotalClaims = claims.Count,
                ApprovedClaims = claims.Count(c => c.Status == "Approved"),
                PendingClaims = claims.Count(c => c.Status == "Pending" || c.Status == "Under Review"),
                RejectedClaims = claims.Count(c => c.Status == "Rejected")
            };
        }
    }
}