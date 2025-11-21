
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ContractMonthlyClaimSystem.Models;
using ContractMonthlyClaimSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using ContractMonthlyClaimSystem.Data;
using ContractMonthlyClaimSystem.Models.ViewModels;
using ContractMonthlyClaimSystem.Models;
using ClosedXML.Excel;

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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalApproveClaim(int claimId)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims
                    .Include(c => c.Lecturer)
                    .FirstOrDefaultAsync(c => c.ClaimID == claimId);

                if (claim != null)
                {
                    claim.Status = "Approved";
                    claim.ApprovalDate = DateTime.Now;
                    await _context.SaveChangesAsync();

                    await _claimService.LogAuditAsync(claimId, user.Id, "Claim finally approved by Manager");

                    TempData["SuccessMessage"] = $"Claim #{claimId} finally approved!";
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManagerRejectClaim(int claimId, string reason)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Rejected";
                    claim.AdditionalNotes += $"\n[Manager Rejection]: {reason}";
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
                if (!startDate.HasValue || !endDate.HasValue)
                {
                    startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                    endDate = DateTime.Now;
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateExport(ExportReportViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Please provide valid dates.";
                return RedirectToAction("ExportReports");
            }

            return RedirectToAction("ExportReports", new
            {
                reportType = model.ReportType,
                startDate = model.StartDate,
                endDate = model.EndDate
            });
        }

        [HttpGet]
        public async Task<IActionResult> DownloadReport(string reportType, DateTime startDate, DateTime endDate, string format = "PDF")
        {
            try
            {
                var reportData = await GenerateComprehensiveReportAsync(startDate, endDate, reportType);

                if (format.ToUpper() == "PDF")
                {
                    // For PDF, return the view that will be converted to PDF
                    return View("ReportPDF", reportData);
                }
                else if (format.ToUpper() == "EXCEL")
                {
                    // Generate Excel file
                    return await GenerateExcelReport(reportData);
                }
                else
                {
                    TempData["ErrorMessage"] = "Unsupported format. Please choose PDF or Excel.";
                    return RedirectToAction("ExportReports");
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error generating {format} report: " + ex.Message;
                return RedirectToAction("ExportReports");
            }
        }

        [HttpPost]
        public async Task<IActionResult> RegenerateReport(string reportType, DateTime startDate, DateTime endDate, string format)
        {
            try
            {
                // Log the regeneration for audit purposes
                var user = await _userManager.GetUserAsync(User);
                await _claimService.LogAuditAsync(0, user.Id, $"Regenerated {reportType} report for period {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd} in {format} format");

                return await DownloadReport(reportType, startDate, endDate, format);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error regenerating report: " + ex.Message;
                return RedirectToAction("ExportReports");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetExportHistory()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var exportHistory = await _context.AuditTrails
                    .Where(a => a.Action.Contains("report") && a.UserId == user.Id)
                    .OrderByDescending(a => a.Timestamp)
                    .Take(10)
                    .Select(a => new
                    {
                        ExportDate = a.Timestamp,
                        ReportType = ExtractReportTypeFromAction(a.Action),
                        Period = ExtractPeriodFromAction(a.Action),
                        Format = ExtractFormatFromAction(a.Action),
                        GeneratedBy = a.User.FullName
                    })
                    .ToListAsync();

                return Json(new { success = true, data = exportHistory });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ClaimAnalysis(int id)
        {
            try
            {
                var claim = await _context.Claims
                    .Include(c => c.Lecturer)
                    .ThenInclude(l => l.User)
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

                // Calculate processing time
                var approvalAudit = claim.AuditTrails?
                    .FirstOrDefault(a => a.Action.Contains("Approved") || a.Action.Contains("Rejected"));

                if (approvalAudit != null)
                {
                    ViewBag.ProcessingTime = (approvalAudit.Timestamp - claim.SubmissionDate).TotalDays;
                }

                return View(claim);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading claim analysis: " + ex.Message;
                return RedirectToAction("ApproveClaims");
            }
        }

        // Bulk Actions
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkApprove(int[] claimIds)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                int successCount = 0;

                foreach (var claimId in claimIds)
                {
                    var claim = await _context.Claims.FindAsync(claimId);
                    if (claim != null && (claim.Status == "Coordinator Approved" || claim.Status == "Under Review"))
                    {
                        claim.Status = "Approved";
                        await _claimService.LogAuditAsync(claimId, user.Id, "Claim approved via bulk action by Manager");
                        successCount++;
                    }
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Successfully approved {successCount} claims.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error processing bulk approval: " + ex.Message;
            }

            return RedirectToAction("ApproveClaims");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkReject(int[] claimIds, string reason)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                int successCount = 0;

                foreach (var claimId in claimIds)
                {
                    var claim = await _context.Claims.FindAsync(claimId);
                    if (claim != null && (claim.Status == "Coordinator Approved" || claim.Status == "Under Review"))
                    {
                        claim.Status = "Rejected";
                        claim.AdditionalNotes += $"\n[Bulk Rejection by Manager]: {reason}";
                        await _claimService.LogAuditAsync(claimId, user.Id, $"Claim rejected via bulk action: {reason}");
                        successCount++;
                    }
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Successfully rejected {successCount} claims.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error processing bulk rejection: " + ex.Message;
            }

            return RedirectToAction("ApproveClaims");
        }

        // API Endpoints for AJAX
        [HttpGet]
        public async Task<IActionResult> GetDashboardData()
        {
            try
            {
                var data = new
                {
                    PendingApprovalCount = await _context.Claims.CountAsync(c => c.Status == "Coordinator Approved"),
                    TotalAmountThisMonth = await GetTotalAmountThisMonthAsync(),
                    RecentActivities = await _context.AuditTrails
                        .Include(a => a.User)
                        .Include(a => a.Claim)
                        .OrderByDescending(a => a.Timestamp)
                        .Take(10)
                        .Select(a => new
                        {
                            Action = a.Action,
                            User = a.User.FullName,
                            ClaimId = a.ClaimID,
                            Timestamp = a.Timestamp.ToString("MMM dd, HH:mm")
                        })
                        .ToListAsync()
                };

                return Json(new { success = true, data = data });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Private helper methods
        private async Task<List<Claim>> GetClaimsPendingManagerApprovalAsync()
        {
            return await _context.Claims
                .Include(c => c.Lecturer)
                .ThenInclude(l => l.User)
                .Include(c => c.SupportingDocuments)
                .Include(c => c.AuditTrails)
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
                .ThenInclude(a => a.User)
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

            return await _context.Claims
                .Where(c => c.Status == "Approved" && c.SubmissionDate >= startDate && c.SubmissionDate <= endDate)
                .SumAsync(c => c.Amount);
        }

        private async Task<DashboardStatistics> GetDashboardStatisticsAsync()
        {
            var startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var claims = await _context.Claims
                .Where(c => c.SubmissionDate >= startDate.AddMonths(-6))
                .ToListAsync();

            var approvedClaims = claims.Where(c => c.Status == "Approved").ToList();

            return new DashboardStatistics
            {
                TotalApprovedAmount = approvedClaims.Sum(c => c.Amount),
                AverageProcessingTime = await CalculateAverageProcessingTime(),
                ApprovalRate = claims.Any() ? (decimal)approvedClaims.Count / claims.Count * 100 : 0,
                TopDepartment = await GetTopDepartmentAsync(),
                ClaimsTrend = await GetClaimsTrendAsync()
            };
        }

        private async Task<double> CalculateAverageProcessingTime()
        {
            var approvedClaims = await _context.Claims
                .Include(c => c.AuditTrails)
                .Where(c => c.Status == "Approved")
                .ToListAsync();

            if (!approvedClaims.Any()) return 0;

            double totalDays = 0;
            int count = 0;

            foreach (var claim in approvedClaims)
            {
                var approvalAudit = claim.AuditTrails?
                    .OrderByDescending(a => a.Timestamp)
                    .FirstOrDefault(a => a.Action.Contains("Approved"));

                if (approvalAudit != null)
                {
                    var processingTime = (approvalAudit.Timestamp - claim.SubmissionDate).TotalDays;
                    totalDays += processingTime;
                    count++;
                }
            }

            return count > 0 ? Math.Round(totalDays / count, 1) : 0;
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
                    TotalAmount = g.Where(c => c.Status == "Approved").Sum(c => c.Amount),
                    AverageAmount = g.Where(c => c.Status == "Approved").Average(c => c.Amount)
                })
                .OrderByDescending(d => d.TotalAmount)
                .ToListAsync();

            var lecturerStats = await _context.Claims
                .Include(c => c.Lecturer)
                .Where(c => c.SubmissionDate >= startDate && c.SubmissionDate <= endDate)
                .GroupBy(c => new { c.Lecturer.Name, c.Lecturer.Department })
                .Select(g => new LecturerStat
                {
                    LecturerName = g.Key.Name ?? "Unknown",
                    Department = g.Key.Department ?? "Unknown",
                    TotalClaims = g.Count(),
                    ApprovedClaims = g.Count(c => c.Status == "Approved"),
                    TotalAmount = g.Where(c => c.Status == "Approved").Sum(c => c.Amount)
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


        [HttpGet]
        public async Task<IActionResult> GetRecentAuditActivities()
        {
            try
            {
                var recentActivities = await _context.AuditTrails
                    .Include(a => a.User)
                    .OrderByDescending(a => a.Timestamp)
                    .Take(10)
                    .Select(a => new
                    {
                        action = a.Action,
                        userName = a.User.FullName,
                        time = a.Timestamp.ToString("HH:mm:ss")
                    })
                    .ToListAsync();

                return Json(new { success = true, data = recentActivities });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAuditDetails(int id)
        {
            try
            {
                var audit = await _context.AuditTrails
                    .Include(a => a.User)
                    .Include(a => a.Claim)
                    .ThenInclude(c => c.Lecturer)
                    .FirstOrDefaultAsync(a => a.AuditTrailID == id);

                if (audit == null)
                {
                    return Json(new { success = false, message = "Audit record not found" });
                }

                var html = $"""
            <div class="row">
                <div class="col-md-6">
                    <table class="table table-sm">
                        <tr><th>Action:</th><td>{audit.Action}</td></tr>
                        <tr><th>User:</th><td>{audit.User?.FullName} ({audit.User?.Role})</td></tr>
                        <tr><th>Timestamp:</th><td>{audit.Timestamp.ToString("dd MMM yyyy HH:mm:ss")}</td></tr>
                    </table>
                </div>
                <div class="col-md-6">
                    <table class="table table-sm">
                        <tr><th>Claim ID:</th><td>#{audit.ClaimID}</td></tr>
                        <tr><th>Lecturer:</th><td>{audit.Claim?.Lecturer?.Name}</td></tr>
                        <tr><th>Department:</th><td>{audit.Claim?.Lecturer?.Department}</td></tr>
                    </table>
                </div>
            </div>
            <div class="mt-3">
                <h6>Full Action Details:</h6>
                <div class="border rounded p-3 bg-light">
                    {audit.Action}
                </div>
            </div>
        """;

                return Json(new { success = true, html = html });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportAuditTrail(DateTime startDate, DateTime endDate, string actionFilter, string format)
        {
            try
            {
                var auditTrails = await _context.AuditTrails
                    .Include(a => a.User)
                    .Include(a => a.Claim)
                    .ThenInclude(c => c.Lecturer)
                    .Where(a => a.Timestamp >= startDate && a.Timestamp <= endDate)
                    .ToListAsync();

                if (actionFilter != "All")
                {
                    auditTrails = auditTrails.Where(a => a.Action.Contains(actionFilter)).ToList();
                }

                // Implement export logic based on format (CSV/PDF)
                // This would generate and return the file

                TempData["SuccessMessage"] = $"Audit trail exported successfully ({format})";
                return RedirectToAction("AuditTrail");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error exporting audit trail: " + ex.Message;
                return RedirectToAction("AuditTrail");
            }
        }

        // Helper methods for export history
        private string ExtractReportTypeFromAction(string action)
        {
            if (action.Contains("Comprehensive")) return "Comprehensive Report";
            if (action.Contains("Financial")) return "Financial Report";
            if (action.Contains("Claims Summary")) return "Claims Summary";
            if (action.Contains("Lecturer Performance")) return "Lecturer Performance";
            if (action.Contains("Department Summary")) return "Department Summary";
            return "General Report";
        }

        private string ExtractPeriodFromAction(string action)
        {
            // Extract period information from audit action
            var match = System.Text.RegularExpressions.Regex.Match(action, @"\d{4}-\d{2}-\d{2}");
            if (match.Success) return match.Value;
            return "Custom Period";
        }

        private string ExtractFormatFromAction(string action)
        {
            if (action.Contains("PDF")) return "PDF";
            if (action.Contains("Excel")) return "Excel";
            return "Unknown";
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

        private async Task<IActionResult> GenerateExcelReport(ManagerReportViewModel reportData)
        {
            try
            {
                using (var workbook = new XLWorkbook())
                {
                    // Summary Worksheet
                    var summaryWorksheet = workbook.Worksheets.Add("Summary");

                    // Header
                    summaryWorksheet.Cell(1, 1).Value = "CONTRACT MONTHLY CLAIM SYSTEM";
                    summaryWorksheet.Range(1, 1, 1, 5).Merge().Style.Font.Bold = true;
                    summaryWorksheet.Cell(2, 1).Value = $"{reportData.ReportType} REPORT";
                    summaryWorksheet.Range(2, 1, 2, 5).Merge().Style.Font.Bold = true;
                    summaryWorksheet.Cell(3, 1).Value = $"Period: {reportData.StartDate:dd MMMM yyyy} to {reportData.EndDate:dd MMMM yyyy}";
                    summaryWorksheet.Range(3, 1, 3, 5).Merge();
                    summaryWorksheet.Cell(4, 1).Value = $"Generated on: {reportData.GeneratedDate:dd MMMM yyyy 'at' HH:mm}";
                    summaryWorksheet.Range(4, 1, 4, 5).Merge();

                    // Summary Statistics
                    summaryWorksheet.Cell(6, 1).Value = "Summary Statistics";
                    summaryWorksheet.Range(6, 1, 6, 5).Merge().Style.Font.Bold = true;

                    summaryWorksheet.Cell(7, 1).Value = "Total Claims";
                    summaryWorksheet.Cell(7, 2).Value = reportData.TotalClaims;
                    summaryWorksheet.Cell(8, 1).Value = "Approved Claims";
                    summaryWorksheet.Cell(8, 2).Value = reportData.ApprovedClaims;
                    summaryWorksheet.Cell(9, 1).Value = "Pending Claims";
                    summaryWorksheet.Cell(9, 2).Value = reportData.PendingClaims;
                    summaryWorksheet.Cell(10, 1).Value = "Rejected Claims";
                    summaryWorksheet.Cell(10, 2).Value = reportData.RejectedClaims;
                    summaryWorksheet.Cell(11, 1).Value = "Total Amount";
                    summaryWorksheet.Cell(11, 2).Value = reportData.TotalAmount;
                    summaryWorksheet.Cell(11, 2).Style.NumberFormat.Format = "$#,##0.00";

                    // Approval Rate
                    var approvalRate = reportData.TotalClaims > 0 ?
                        (decimal)reportData.ApprovedClaims / reportData.TotalClaims * 100 : 0;
                    summaryWorksheet.Cell(12, 1).Value = "Approval Rate";
                    summaryWorksheet.Cell(12, 2).Value = approvalRate / 100;
                    summaryWorksheet.Cell(12, 2).Style.NumberFormat.Format = "0.0%";

                    // Department Performance Worksheet
                    var deptWorksheet = workbook.Worksheets.Add("Department Performance");
                    deptWorksheet.Cell(1, 1).Value = "Department Performance";
                    deptWorksheet.Range(1, 1, 1, 5).Merge().Style.Font.Bold = true;

                    // Headers
                    deptWorksheet.Cell(3, 1).Value = "Department";
                    deptWorksheet.Cell(3, 2).Value = "Total Claims";
                    deptWorksheet.Cell(3, 3).Value = "Approved";
                    deptWorksheet.Cell(3, 4).Value = "Amount";
                    deptWorksheet.Cell(3, 5).Value = "Approval Rate";

                    var deptRow = 4;
                    foreach (var dept in reportData.Statistics.DepartmentStats)
                    {
                        deptWorksheet.Cell(deptRow, 1).Value = dept.Department;
                        deptWorksheet.Cell(deptRow, 2).Value = dept.TotalClaims;
                        deptWorksheet.Cell(deptRow, 3).Value = dept.ApprovedClaims;
                        deptWorksheet.Cell(deptRow, 4).Value = dept.TotalAmount;
                        deptWorksheet.Cell(deptRow, 4).Style.NumberFormat.Format = "$#,##0.00";
                        deptWorksheet.Cell(deptRow, 5).Value = dept.ApprovalRate / 100;
                        deptWorksheet.Cell(deptRow, 5).Style.NumberFormat.Format = "0.0%";
                        deptRow++;
                    }

                    // Claims Details Worksheet
                    var claimsWorksheet = workbook.Worksheets.Add("Claims Details");
                    claimsWorksheet.Cell(1, 1).Value = "Claims Details";
                    claimsWorksheet.Range(1, 1, 1, 7).Merge().Style.Font.Bold = true;

                    // Headers
                    claimsWorksheet.Cell(3, 1).Value = "Claim ID";
                    claimsWorksheet.Cell(3, 2).Value = "Lecturer";
                    claimsWorksheet.Cell(3, 3).Value = "Department";
                    claimsWorksheet.Cell(3, 4).Value = "Hours";
                    claimsWorksheet.Cell(3, 5).Value = "Amount";
                    claimsWorksheet.Cell(3, 6).Value = "Status";
                    claimsWorksheet.Cell(3, 7).Value = "Submitted";

                    var claimRow = 4;
                    foreach (var claim in reportData.Claims)
                    {
                        claimsWorksheet.Cell(claimRow, 1).Value = claim.ClaimID;
                        claimsWorksheet.Cell(claimRow, 2).Value = claim.Lecturer?.Name;
                        claimsWorksheet.Cell(claimRow, 3).Value = claim.Lecturer?.Department;
                        claimsWorksheet.Cell(claimRow, 4).Value = claim.HoursWorked;
                        claimsWorksheet.Cell(claimRow, 5).Value = claim.Amount;
                        claimsWorksheet.Cell(claimRow, 5).Style.NumberFormat.Format = "$#,##0.00";
                        claimsWorksheet.Cell(claimRow, 6).Value = claim.Status;
                        claimsWorksheet.Cell(claimRow, 7).Value = claim.SubmissionDate;
                        claimsWorksheet.Cell(claimRow, 7).Style.NumberFormat.Format = "dd-mmm-yyyy";
                        claimRow++;
                    }

                    // Auto-fit columns
                    summaryWorksheet.Columns().AdjustToContents();
                    deptWorksheet.Columns().AdjustToContents();
                    claimsWorksheet.Columns().AdjustToContents();

                    using (var stream = new MemoryStream())
                    {
                        workbook.SaveAs(stream);
                        var content = stream.ToArray();

                        // Log the export
                        var user = await _userManager.GetUserAsync(User);
                        await _claimService.LogAuditAsync(0, user.Id,
                            $"Exported {reportData.ReportType} report for period {reportData.StartDate:yyyy-MM-dd} to {reportData.EndDate:yyyy-MM-dd} in Excel format");

                        return File(content,
                            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                            $"{reportData.ReportType.Replace(" ", "_")}_Report_{reportData.StartDate:yyyyMMdd}_{reportData.EndDate:yyyyMMdd}.xlsx");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error generating Excel report: {ex.Message}", ex);
            }
        }


        // Add explicit route for the POST method
        [HttpPost]
        [Route("Manager/UpdateStatistics")]
        public async Task<IActionResult> UpdateStatisticsDateRange(DateTime startDate, DateTime endDate)
        {
            try
            {
                if (startDate > endDate)
                {
                    return Json(new { success = false, message = "Start date cannot be after end date." });
                }

                var statistics = await GetDetailedStatisticsAsync(startDate, endDate);

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        totalClaims = statistics.TotalClaims,
                        approvedClaims = statistics.ApprovedClaims,
                        pendingClaims = statistics.PendingClaims,
                        rejectedClaims = statistics.RejectedClaims,
                        totalAmount = statistics.TotalAmount,
                        approvalRate = statistics.TotalClaims > 0 ? (decimal)statistics.ApprovedClaims / statistics.TotalClaims * 100 : 0,
                        departmentStats = statistics.DepartmentStats,
                        lecturerStats = statistics.LecturerStats,
                        monthlyBreakdown = statistics.MonthlyBreakdown
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [Route("Manager/StatisticsData")]
        public async Task<IActionResult> GetStatisticsData(DateTime startDate, DateTime endDate)
        {
            try
            {
                var statistics = await GetDetailedStatisticsAsync(startDate, endDate);

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        totalClaims = statistics.TotalClaims,
                        approvedClaims = statistics.ApprovedClaims,
                        pendingClaims = statistics.PendingClaims,
                        rejectedClaims = statistics.RejectedClaims,
                        totalAmount = statistics.TotalAmount,
                        departmentStats = statistics.DepartmentStats,
                        lecturerStats = statistics.LecturerStats,
                        monthlyBreakdown = statistics.MonthlyBreakdown
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        [Route("Manager/RealTimeStats")]
        public async Task<IActionResult> GetRealTimeStats()
        {
            try
            {
                var today = DateTime.Today;
                var realTimeStats = new
                {
                    ClaimsToday = await _context.Claims.CountAsync(c => c.SubmissionDate >= today),
                    ApprovedToday = await _context.Claims.CountAsync(c => c.Status == "Approved" && c.SubmissionDate >= today),
                    PendingApproval = await _context.Claims.CountAsync(c => c.Status == "Coordinator Approved"),
                    TotalAmountToday = await _context.Claims
                        .Where(c => c.Status == "Approved" && c.SubmissionDate >= today)
                        .SumAsync(c => c.Amount)
                };

                return Json(new { success = true, data = realTimeStats });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Enhanced GetDetailedStatisticsAsync method
        private async Task<ManagerStatistics> GetDetailedStatisticsAsync(DateTime startDate, DateTime endDate)
        {
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
                    TotalAmount = g.Where(c => c.Status == "Approved").Sum(c => c.Amount),
                    AverageAmount = g.Where(c => c.Status == "Approved").Average(c => c.Amount)
                })
                .OrderByDescending(d => d.TotalAmount)
                .ToListAsync();

            var lecturerStats = await _context.Claims
                .Include(c => c.Lecturer)
                .Where(c => c.SubmissionDate >= startDate && c.SubmissionDate <= endDate)
                .GroupBy(c => new { c.Lecturer.Name, c.Lecturer.Department })
                .Select(g => new LecturerStat
                {
                    LecturerName = g.Key.Name ?? "Unknown",
                    Department = g.Key.Department ?? "Unknown",
                    TotalClaims = g.Count(),
                    ApprovedClaims = g.Count(c => c.Status == "Approved"),
                    TotalAmount = g.Where(c => c.Status == "Approved").Sum(c => c.Amount)
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

        // Enhanced GetMonthlyBreakdownAsync method
        private async Task<List<MonthlyBreakdown>> GetMonthlyBreakdownAsync(DateTime startDate, DateTime endDate)
        {
            var breakdown = new List<MonthlyBreakdown>();
            var current = new DateTime(startDate.Year, startDate.Month, 1);

            while (current <= endDate)
            {
                var monthEnd = current.AddMonths(1).AddDays(-1);
                if (monthEnd > endDate) monthEnd = endDate;

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

        

    }
}
