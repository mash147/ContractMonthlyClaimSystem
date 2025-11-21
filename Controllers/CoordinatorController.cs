using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ContractMonthlyClaimSystem.Data;
using ContractMonthlyClaimSystem.Models;
using ContractMonthlyClaimSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace ContractMonthlyClaimSystem.Controllers
{
    [Authorize(Roles = "Coordinator,Manager")]
    public class CoordinatorController : Controller
    {
        private readonly IClaimService _claimService;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;

        public CoordinatorController(
            IClaimService claimService,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment)
        {
            _claimService = claimService;
            _context = context;
            _userManager = userManager;
            _environment = environment;
        }

        // Dashboard
        public async Task<IActionResult> Dashboard()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var pendingClaims = await GetClaimsByStatusAsync("Pending");

                // Populate all required ViewBag properties with null checks
                ViewBag.UserName = user?.FullName ?? User.Identity?.Name ?? "Coordinator";
                ViewBag.UserRole = User.IsInRole("Manager") ? "Academic Manager" : "Programme Coordinator";
                ViewBag.PendingClaimsCount = pendingClaims?.Count ?? 0;
                ViewBag.TotalClaimsCount = await _context.Claims.CountAsync();
                ViewBag.ApprovedThisMonth = await GetApprovedThisMonthCountAsync();
                ViewBag.DocumentsToVerify = await GetDocumentsToVerifyCountAsync();
                ViewBag.UnderReviewCount = await _context.Claims.CountAsync(c => c.Status == "Under Review");
                ViewBag.RevisionRequestedCount = await _context.Claims.CountAsync(c => c.Status == "Revision Requested");
                ViewBag.RejectedCount = await _context.Claims.CountAsync(c => c.Status == "Rejected");

                // Total amount for approved claims
                ViewBag.TotalAmount = await _context.Claims
                    .Where(c => c.Status == "Approved" || c.Status == "Coordinator Approved")
                    .SumAsync(c => c.Amount);

                return View(pendingClaims ?? new List<Claim>());
            }
            catch (Exception ex)
            {
                // Log the exception
                TempData["ErrorMessage"] = "Error loading dashboard: " + ex.Message;

                // Set default values to avoid view errors
                SetDefaultViewBagValues();
                return View(new List<Claim>());
            }
        }

        private void SetDefaultViewBagValues()
        {
            ViewBag.UserName = User.Identity?.Name ?? "Coordinator";
            ViewBag.UserRole = User.IsInRole("Manager") ? "Academic Manager" : "Programme Coordinator";
            ViewBag.PendingClaimsCount = 0;
            ViewBag.TotalClaimsCount = 0;
            ViewBag.ApprovedThisMonth = 0;
            ViewBag.DocumentsToVerify = 0;
            ViewBag.UnderReviewCount = 0;
            ViewBag.RevisionRequestedCount = 0;
            ViewBag.RejectedCount = 0;
            ViewBag.TotalAmount = 0m;
        }

        // Review Claims - List View
        [HttpGet]
        public async Task<IActionResult> ReviewClaims(string status = "Pending")
        {
            try
            {
                var claims = await GetClaimsByStatusAsync(status);
                ViewBag.CurrentStatus = status;
                ViewBag.StatusList = new List<string> { "Pending", "Under Review", "Coordinator Approved", "Rejected", "Revision Requested" };

                return View(claims);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading claims: " + ex.Message;
                return View(new List<Claim>());
            }
        }

        // Review Claim - Detail View
        [HttpGet]
        public async Task<IActionResult> ReviewClaim(int id)
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
                    return RedirectToAction("ReviewClaims");
                }

                return View(claim);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading claim: " + ex.Message;
                return RedirectToAction("ReviewClaims");
            }
        }

        // Forward to Manager (Approve)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForwardToManager(int claimId, string notes)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Coordinator Approved";
                    if (!string.IsNullOrEmpty(notes))
                    {
                        claim.AdditionalNotes += $"\n[Coordinator Notes]: {notes}";
                    }

                    await _context.SaveChangesAsync();
                    await _claimService.LogAuditAsync(claimId, user.Id, $"Claim forwarded to Manager: {notes}");

                    TempData["SuccessMessage"] = "Claim forwarded to manager successfully!";
                }
                else
                {
                    TempData["ErrorMessage"] = "Claim not found.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error forwarding claim: " + ex.Message;
            }

            return RedirectToAction("ReviewClaims");
        }

        // Request Revision
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestRevision(int claimId, string revisionReason)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Revision Requested";
                    claim.AdditionalNotes += $"\n[Revision Required]: {revisionReason}";

                    await _context.SaveChangesAsync();
                    await _claimService.LogAuditAsync(claimId, user.Id, $"Revision requested: {revisionReason}");

                    TempData["SuccessMessage"] = "Revision requested successfully! The lecturer will be notified.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Claim not found.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error requesting revision: " + ex.Message;
            }

            return RedirectToAction("ReviewClaims");
        }

        // Reject Claim
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectClaim(int claimId, string reason)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Rejected";
                    claim.AdditionalNotes += $"\n[Rejection Reason]: {reason}";

                    await _context.SaveChangesAsync();
                    await _claimService.LogAuditAsync(claimId, user.Id, $"Claim rejected: {reason}");

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

            return RedirectToAction("ReviewClaims");
        }

        // Set Under Review
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetUnderReview(int claimId)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Under Review";
                    await _context.SaveChangesAsync();
                    await _claimService.LogAuditAsync(claimId, user.Id, "Claim marked as Under Review");

                    TempData["SuccessMessage"] = "Claim marked as Under Review.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Claim not found.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error updating claim status: " + ex.Message;
            }

            return RedirectToAction("ReviewClaims");
        }

        // Verify Documents
        [HttpGet]
        public async Task<IActionResult> VerifyDocuments(int? claimId, string status = "All")
        {
            try
            {
                var query = _context.Claims
                    .Include(c => c.Lecturer)
                    .Include(c => c.SupportingDocuments)
                    .Where(c => c.SupportingDocuments.Any());

                if (claimId.HasValue)
                {
                    query = query.Where(c => c.ClaimID == claimId.Value);
                    ViewBag.SelectedClaimId = claimId.Value;
                }

                if (status != "All")
                {
                    query = query.Where(c => c.Status == status);
                }

                var claims = await query
                    .OrderByDescending(c => c.SubmissionDate)
                    .ToListAsync();

                ViewBag.StatusFilter = status;
                ViewBag.StatusList = new List<string> { "All", "Pending", "Under Review", "Coordinator Approved", "Revision Requested" };

                return View(claims);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading documents: " + ex.Message;
                return View(new List<Claim>());
            }
        }

        // Verify Single Document
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyDocument(int documentId, bool isValid, string notes)
        {
            int? claimId = null;

            try
            {
                var document = await _context.SupportingDocuments
                    .Include(d => d.Claim)
                    .FirstOrDefaultAsync(d => d.DocumentID == documentId);

                if (document != null)
                {
                    claimId = document.ClaimID; // Store the claim ID before using it

                    var user = await _userManager.GetUserAsync(User);
                    var action = isValid ? "Document Verified" : "Document Rejected";
                    var message = $"{action}: {document.FileName}";

                    if (!string.IsNullOrEmpty(notes))
                    {
                        message += $" - Notes: {notes}";
                    }

                    await _claimService.LogAuditAsync(document.ClaimID, user.Id, message);

                    TempData["SuccessMessage"] = $"Document {(isValid ? "verified" : "rejected")} successfully!";
                }
                else
                {
                    TempData["ErrorMessage"] = "Document not found.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error verifying document: " + ex.Message;
            }

            return RedirectToAction("VerifyDocuments", new { claimId = claimId });
        }

        [HttpGet]
        public async Task<IActionResult> GenerateReports(string reportType = "Monthly", DateTime? startDate = null, DateTime? endDate = null, string department = "All")
        {
            try
            {
                // Set default date range based on report type
                if (!startDate.HasValue || !endDate.HasValue)
                {
                    (startDate, endDate) = GetDefaultDateRange(reportType);
                }

                var query = _context.Claims
                    .Include(c => c.Lecturer)
                    .Include(c => c.SupportingDocuments)
                    .Where(c => c.SubmissionDate >= startDate && c.SubmissionDate <= endDate);

                if (department != "All")
                {
                    query = query.Where(c => c.Lecturer.Department == department);
                }

                var claims = await query
                    .OrderByDescending(c => c.SubmissionDate)
                    .ToListAsync();

                var reportData = new CoordinatorReportViewModel
                {
                    ReportType = reportType,
                    StartDate = startDate.Value,
                    EndDate = endDate.Value,
                    Department = department,
                    Claims = claims,
                    TotalAmount = claims.Where(c => c.Status == "Approved" || c.Status == "Coordinator Approved").Sum(c => c.Amount),
                    TotalClaims = claims.Count,
                    ApprovedClaims = claims.Count(c => c.Status == "Approved" || c.Status == "Coordinator Approved"),
                    PendingClaims = claims.Count(c => c.Status == "Pending"),
                    RejectedClaims = claims.Count(c => c.Status == "Rejected"),
                    UnderReviewClaims = claims.Count(c => c.Status == "Under Review" || c.Status == "Revision Requested"),
                    AverageProcessingTime = await CalculateAverageProcessingTime(claims)
                };

                // Calculate additional metrics
                reportData.ApprovalRate = reportData.TotalClaims > 0 ?
                    (decimal)reportData.ApprovedClaims / reportData.TotalClaims * 100 : 0;
                reportData.AverageClaimAmount = reportData.ApprovedClaims > 0 ?
                    reportData.TotalAmount / reportData.ApprovedClaims : 0;

                ViewBag.ReportTypes = new List<string> { "Monthly", "Weekly", "Quarterly", "Custom" };
                ViewBag.Departments = await GetDepartmentsListAsync();

                // Add chart data to ViewBag
                ViewBag.ChartData = GetChartData(claims, startDate.Value, endDate.Value, reportType);

                return View(reportData);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error generating report: " + ex.Message;
                return View(new CoordinatorReportViewModel());
            }
        }

        // Export report data as JSON for charts
        [HttpGet]
        public async Task<IActionResult> GetReportData(string reportType = "Monthly", DateTime? startDate = null, DateTime? endDate = null, string department = "All")
        {
            try
            {
                if (!startDate.HasValue || !endDate.HasValue)
                {
                    (startDate, endDate) = GetDefaultDateRange(reportType);
                }

                var query = _context.Claims
                    .Include(c => c.Lecturer)
                    .Where(c => c.SubmissionDate >= startDate && c.SubmissionDate <= endDate);

                if (department != "All")
                {
                    query = query.Where(c => c.Lecturer.Department == department);
                }

                var claims = await query.ToListAsync();

                var chartData = GetChartData(claims, startDate.Value, endDate.Value, reportType);

                return Json(new { success = true, data = chartData });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Export Report
        [HttpGet]
        public async Task<IActionResult> ExportReport(string reportType, DateTime startDate, DateTime endDate, string department = "All", string format = "PDF")
        {
            try
            {
                var query = _context.Claims
                    .Include(c => c.Lecturer)
                    .Where(c => c.SubmissionDate >= startDate && c.SubmissionDate <= endDate);

                if (department != "All")
                {
                    query = query.Where(c => c.Lecturer.Department == department);
                }

                var claims = await query
                    .OrderByDescending(c => c.SubmissionDate)
                    .ToListAsync();

                var reportData = new CoordinatorReportViewModel
                {
                    ReportType = reportType,
                    StartDate = startDate,
                    EndDate = endDate,
                    Department = department,
                    Claims = claims,
                    TotalAmount = claims.Where(c => c.Status == "Approved" || c.Status == "Coordinator Approved").Sum(c => c.Amount),
                    TotalClaims = claims.Count,
                    ApprovedClaims = claims.Count(c => c.Status == "Approved" || c.Status == "Coordinator Approved"),
                    PendingClaims = claims.Count(c => c.Status == "Pending"),
                    RejectedClaims = claims.Count(c => c.Status == "Rejected"),
                    UnderReviewClaims = claims.Count(c => c.Status == "Under Review" || c.Status == "Revision Requested"),
                    AverageProcessingTime = await CalculateAverageProcessingTime(claims)
                };

                reportData.ApprovalRate = reportData.TotalClaims > 0 ?
                    (decimal)reportData.ApprovedClaims / reportData.TotalClaims * 100 : 0;
                reportData.AverageClaimAmount = reportData.ApprovedClaims > 0 ?
                    reportData.TotalAmount / reportData.ApprovedClaims : 0;

                if (format == "PDF")
                {
                    // For now, return view - you can implement proper PDF generation later
                    return View("CoordinatorReportPDF", reportData);
                }
                else
                {
                    // For now, return view - you can implement proper Excel generation later
                    return View("CoordinatorReportExcel", reportData);
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error exporting report: " + ex.Message;
                return RedirectToAction("GenerateReports");
            }
        }

        [HttpPost]
        public async Task<IActionResult> GenerateReport(CoordinatorReportViewModel model)
        {
            return RedirectToAction("GenerateReports", new
            {
                reportType = model.ReportType,
                startDate = model.StartDate,
                endDate = model.EndDate,
                department = model.Department
            });
        }

        // Export Report


        // Download Document
        [HttpGet]
        public async Task<IActionResult> DownloadDocument(int documentId)
        {
            try
            {
                var document = await _context.SupportingDocuments.FindAsync(documentId);
                if (document == null)
                {
                    TempData["ErrorMessage"] = "Document not found.";
                    return RedirectToAction("VerifyDocuments");
                }

                var filePath = Path.Combine(_environment.WebRootPath, "documents", document.FilePath);
                if (!System.IO.File.Exists(filePath))
                {
                    TempData["ErrorMessage"] = "File not found on server.";
                    return RedirectToAction("VerifyDocuments");
                }

                var memory = new MemoryStream();
                using (var stream = new FileStream(filePath, FileMode.Open))
                {
                    await stream.CopyToAsync(memory);
                }
                memory.Position = 0;

                return File(memory, GetContentType(document.FilePath), document.FileName);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error downloading document: " + ex.Message;
                return RedirectToAction("VerifyDocuments");
            }
        }

        // Quick Actions
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickApprove(int claimId)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Coordinator Approved";
                    await _context.SaveChangesAsync();
                    await _claimService.LogAuditAsync(claimId, user.Id, "Claim quickly approved by coordinator");

                    TempData["SuccessMessage"] = "Claim approved successfully!";
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

            return RedirectToAction("ReviewClaims");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickReject(int claimId)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Rejected";
                    claim.AdditionalNotes += $"\n[Quick Rejection]: Rejected by coordinator";
                    await _context.SaveChangesAsync();
                    await _claimService.LogAuditAsync(claimId, user.Id, "Claim quickly rejected by coordinator");

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

            return RedirectToAction("ReviewClaims");
        }

        // Statistics API
        [HttpGet]
        public async Task<IActionResult> GetStatistics()
        {
            try
            {
                var statistics = new
                {
                    TotalClaims = await _context.Claims.CountAsync(),
                    PendingClaims = await _context.Claims.CountAsync(c => c.Status == "Pending"),
                    ApprovedClaims = await _context.Claims.CountAsync(c => c.Status == "Approved" || c.Status == "Coordinator Approved"),
                    RejectedClaims = await _context.Claims.CountAsync(c => c.Status == "Rejected"),
                    TotalAmount = await _context.Claims.Where(c => c.Status == "Approved" || c.Status == "Coordinator Approved").SumAsync(c => c.Amount)
                };

                return Json(new { success = true, data = statistics });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Request Document Revision
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestDocumentRevision(int claimId, int documentId, string revisionReason)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var document = await _context.SupportingDocuments
                    .Include(d => d.Claim)
                    .FirstOrDefaultAsync(d => d.DocumentID == documentId);

                if (document != null)
                {
                    var message = $"Document revision requested: {document.FileName} - Reason: {revisionReason}";
                    await _claimService.LogAuditAsync(claimId, user.Id, message);

                    TempData["SuccessMessage"] = "Document revision requested successfully!";
                }
                else
                {
                    TempData["ErrorMessage"] = "Document not found.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error requesting document revision: " + ex.Message;
            }

            return RedirectToAction("ReviewClaim", new { id = claimId });
        }

        // Batch Verify Documents
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BatchVerifyDocuments(int claimId, string documentActions)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims
                    .Include(c => c.SupportingDocuments)
                    .FirstOrDefaultAsync(c => c.ClaimID == claimId);

                if (claim != null)
                {
                    // Parse document actions (format: "docId:action:notes|docId:action:notes")
                    var actions = documentActions.Split('|')
                        .Select(action => action.Split(':'))
                        .Where(parts => parts.Length >= 2);

                    foreach (var actionParts in actions)
                    {
                        if (int.TryParse(actionParts[0], out int docId))
                        {
                            var document = claim.SupportingDocuments.FirstOrDefault(d => d.DocumentID == docId);
                            if (document != null)
                            {
                                var isValid = actionParts[1] == "verify";
                                var notes = actionParts.Length > 2 ? Uri.UnescapeDataString(actionParts[2]) : "";
                                var action = isValid ? "Document Verified" : "Document Rejected";
                                var message = $"{action}: {document.FileName}";

                                if (!string.IsNullOrEmpty(notes))
                                {
                                    message += $" - Notes: {notes}";
                                }

                                await _claimService.LogAuditAsync(claimId, user.Id, message);
                            }
                        }
                    }

                    TempData["SuccessMessage"] = "Documents processed successfully!";
                }
                else
                {
                    TempData["ErrorMessage"] = "Claim not found.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error processing documents: " + ex.Message;
            }

            return RedirectToAction("VerifyDocuments", new { claimId = claimId });
        }

        // Helper Methods
        private async Task<List<Claim>> GetClaimsByStatusAsync(string status)
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

        private async Task<int> GetApprovedThisMonthCountAsync()
        {
            var startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            return await _context.Claims
                .CountAsync(c => (c.Status == "Approved" || c.Status == "Coordinator Approved") &&
                                c.SubmissionDate >= startDate &&
                                c.SubmissionDate <= endDate);
        }

        private async Task<int> GetDocumentsToVerifyCountAsync()
        {
            return await _context.SupportingDocuments
                .Include(d => d.Claim)
                .Where(d => d.Claim.Status == "Pending" || d.Claim.Status == "Under Review")
                .CountAsync();
        }

        private async Task<List<string>> GetDepartmentsListAsync()
        {
            var departments = await _context.Lecturers
                .Where(l => l.Department != null)
                .Select(l => l.Department)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync();

            departments.Insert(0, "All");
            return departments;
        }

        private async Task<double> CalculateAverageProcessingTime(List<Claim> claims)
        {
            var processedClaims = claims.Where(c => c.Status == "Approved" || c.Status == "Coordinator Approved" || c.Status == "Rejected");

            if (!processedClaims.Any())
                return 0;

            double totalDays = 0;
            int count = 0;

            foreach (var claim in processedClaims)
            {
                var approvalAudit = claim.AuditTrails?
                    .OrderByDescending(a => a.Timestamp)
                    .FirstOrDefault(a => a.Action.Contains("Approved") || a.Action.Contains("Rejected"));

                if (approvalAudit != null)
                {
                    var processingTime = (approvalAudit.Timestamp - claim.SubmissionDate).TotalDays;
                    totalDays += processingTime;
                    count++;
                }
            }

            return count > 0 ? Math.Round(totalDays / count, 1) : 0;
        }

        private string GetContentType(string path)
        {
            var types = new Dictionary<string, string>
            {
                { ".pdf", "application/pdf" },
                { ".doc", "application/msword" },
                { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
                { ".xls", "application/vnd.ms-excel" },
                { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
                { ".jpg", "image/jpeg" },
                { ".jpeg", "image/jpeg" },
                { ".png", "image/png" }
            };

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return types.ContainsKey(ext) ? types[ext] : "application/octet-stream";
        }

        // Quick Approve from ReviewClaim page
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickApproveFromReview(int claimId)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Coordinator Approved";
                    await _context.SaveChangesAsync();
                    await _claimService.LogAuditAsync(claimId, user.Id, "Claim approved from review page");

                    TempData["SuccessMessage"] = "Claim approved successfully!";
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

            return RedirectToAction("ReviewClaim", new { id = claimId });
        }

        // Quick Reject from ReviewClaim page
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickRejectFromReview(int claimId, string reason)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Rejected";
                    claim.AdditionalNotes += $"\n[Rejection Reason]: {reason}";
                    await _context.SaveChangesAsync();
                    await _claimService.LogAuditAsync(claimId, user.Id, $"Claim rejected from review page: {reason}");

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

            return RedirectToAction("ReviewClaim", new { id = claimId });
        }

        // Batch verify all documents for a claim
        // Add these methods to CoordinatorController

        // Enhanced document verification with bulk actions
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyAllDocuments(int claimId, string notes)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims
                    .Include(c => c.SupportingDocuments)
                    .FirstOrDefaultAsync(c => c.ClaimID == claimId);

                if (claim != null && claim.SupportingDocuments.Any())
                {
                    foreach (var document in claim.SupportingDocuments)
                    {
                        var message = $"Document Verified: {document.FileName}";
                        if (!string.IsNullOrEmpty(notes))
                        {
                            message += $" - Notes: {notes}";
                        }
                        await _claimService.LogAuditAsync(claimId, user.Id, message);
                    }

                    TempData["SuccessMessage"] = $"All {claim.SupportingDocuments.Count} documents verified successfully!";
                }
                else
                {
                    TempData["ErrorMessage"] = "No documents found to verify.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error verifying documents: " + ex.Message;
            }

            return RedirectToAction("VerifyDocuments", new { claimId = claimId });
        }

        // Global batch verification
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GlobalBatchVerify(int[] documentIds, string notes, bool isValid)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                int successCount = 0;

                foreach (var documentId in documentIds)
                {
                    var document = await _context.SupportingDocuments
                        .Include(d => d.Claim)
                        .FirstOrDefaultAsync(d => d.DocumentID == documentId);

                    if (document != null)
                    {
                        var action = isValid ? "Document Verified" : "Document Rejected";
                        var message = $"{action}: {document.FileName}";

                        if (!string.IsNullOrEmpty(notes))
                        {
                            message += $" - Notes: {notes}";
                        }

                        await _claimService.LogAuditAsync(document.ClaimID, user.Id, message);
                        successCount++;
                    }
                }

                TempData["SuccessMessage"] = $"Successfully processed {successCount} documents.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error processing documents: " + ex.Message;
            }

            return RedirectToAction("VerifyDocuments");
        }

        // Document preview endpoint
        [HttpGet]
        public async Task<IActionResult> PreviewDocument(int documentId)
        {
            try
            {
                var document = await _context.SupportingDocuments
                    .Include(d => d.Claim)
                    .FirstOrDefaultAsync(d => d.DocumentID == documentId);

                if (document == null)
                {
                    return Json(new { success = false, message = "Document not found." });
                }

                var filePath = Path.Combine(_environment.WebRootPath, "documents", document.FilePath);
                if (!System.IO.File.Exists(filePath))
                {
                    return Json(new { success = false, message = "File not found on server." });
                }

                // For now, return document info - you can implement actual preview later
                return Json(new
                {
                    success = true,
                    documentName = document.FileName,
                    fileType = Path.GetExtension(document.FileName).ToLower(),
                    fileSize = new FileInfo(filePath).Length,
                    uploadDate = document.UploadedDate.ToString("dd MMM yyyy HH:mm")
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Get document statistics for the page
        [HttpGet]
        public async Task<IActionResult> GetDocumentStats(string status = "All")
        {
            try
            {
                var query = _context.Claims
                    .Include(c => c.SupportingDocuments)
                    .Where(c => c.SupportingDocuments.Any());

                if (status != "All")
                {
                    query = query.Where(c => c.Status == status);
                }

                var claims = await query.ToListAsync();

                var stats = new
                {
                    TotalDocuments = claims.Sum(c => c.SupportingDocuments.Count),
                    TotalClaims = claims.Count,
                    PendingDocuments = claims
                        .Where(c => c.Status == "Pending" || c.Status == "Under Review")
                        .Sum(c => c.SupportingDocuments.Count),
                    VerifiedThisWeek = await _context.AuditTrails
                        .Where(a => a.Action.Contains("Document Verified") &&
                                   a.Timestamp >= DateTime.Now.AddDays(-7))
                        .CountAsync()
                };

                return Json(new { success = true, data = stats });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Bulk Actions for Review Claims
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
                    if (claim != null && (claim.Status == "Pending" || claim.Status == "Under Review"))
                    {
                        claim.Status = "Coordinator Approved";
                        await _claimService.LogAuditAsync(claimId, user.Id, "Claim approved via bulk action");
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

            return RedirectToAction("ReviewClaims");
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
                    if (claim != null && (claim.Status == "Pending" || claim.Status == "Under Review"))
                    {
                        claim.Status = "Rejected";
                        claim.AdditionalNotes += $"\n[Bulk Rejection]: {reason}";
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

            return RedirectToAction("ReviewClaims");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkSetUnderReview(int[] claimIds)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                int successCount = 0;

                foreach (var claimId in claimIds)
                {
                    var claim = await _context.Claims.FindAsync(claimId);
                    if (claim != null && claim.Status == "Pending")
                    {
                        claim.Status = "Under Review";
                        await _claimService.LogAuditAsync(claimId, user.Id, "Claim marked as Under Review via bulk action");
                        successCount++;
                    }
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Successfully marked {successCount} claims as Under Review.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error processing bulk action: " + ex.Message;
            }

            return RedirectToAction("ReviewClaims");
        }

        // Quick Actions for individual claims
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickApproveFromList(int claimId)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Coordinator Approved";
                    await _context.SaveChangesAsync();
                    await _claimService.LogAuditAsync(claimId, user.Id, "Claim quickly approved from list view");

                    TempData["SuccessMessage"] = $"Claim #{claimId} approved successfully!";
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

            return RedirectToAction("ReviewClaims", new { status = ViewBag.CurrentStatus });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickRejectFromList(int claimId, string reason)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                var claim = await _context.Claims.FindAsync(claimId);

                if (claim != null)
                {
                    claim.Status = "Rejected";
                    claim.AdditionalNotes += $"\n[Quick Rejection]: {reason}";
                    await _context.SaveChangesAsync();
                    await _claimService.LogAuditAsync(claimId, user.Id, $"Claim quickly rejected from list view: {reason}");

                    TempData["SuccessMessage"] = $"Claim #{claimId} rejected successfully!";
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

            return RedirectToAction("ReviewClaims", new { status = ViewBag.CurrentStatus });
        }

        // Helper methods for report generation
        private (DateTime startDate, DateTime endDate) GetDefaultDateRange(string reportType)
        {
            var now = DateTime.Now;

            return reportType switch
            {
                "Weekly" => (now.AddDays(-7), now),
                "Monthly" => (new DateTime(now.Year, now.Month, 1), now),
                "Quarterly" => (new DateTime(now.Year, ((now.Month - 1) / 3) * 3 + 1, 1), now),
                "Custom" => (now.AddDays(-30), now),
                _ => (now.AddDays(-30), now)
            };
        }

        private object GetChartData(List<Claim> claims, DateTime startDate, DateTime endDate, string reportType)
        {
            // Status distribution
            var statusData = new
            {
                labels = new[] { "Approved", "Pending", "Under Review", "Rejected", "Revision Requested" },
                data = new[]
                {
            claims.Count(c => c.Status == "Approved" || c.Status == "Coordinator Approved"),
            claims.Count(c => c.Status == "Pending"),
            claims.Count(c => c.Status == "Under Review"),
            claims.Count(c => c.Status == "Rejected"),
            claims.Count(c => c.Status == "Revision Requested")
        },
                colors = new[] { "#28a745", "#ffc107", "#17a2b8", "#dc3545", "#6c757d" }
            };

            // Monthly trends
            var monthlyData = claims
                .GroupBy(c => new { c.SubmissionDate.Year, c.SubmissionDate.Month })
                .OrderBy(g => g.Key.Year)
                .ThenBy(g => g.Key.Month)
                .Select(g => new
                {
                    period = $"{g.Key.Year}-{g.Key.Month:00}",
                    approved = g.Count(c => c.Status == "Approved" || c.Status == "Coordinator Approved"),
                    pending = g.Count(c => c.Status == "Pending"),
                    amount = g.Where(c => c.Status == "Approved" || c.Status == "Coordinator Approved").Sum(c => c.Amount)
                })
                .ToList();

            // Department distribution
            var departmentData = claims
                .Where(c => c.Lecturer != null && !string.IsNullOrEmpty(c.Lecturer.Department))
                .GroupBy(c => c.Lecturer.Department)
                .Select(g => new
                {
                    department = g.Key,
                    count = g.Count(),
                    amount = g.Where(c => c.Status == "Approved" || c.Status == "Coordinator Approved").Sum(c => c.Amount)
                })
                .OrderByDescending(g => g.count)
                .ToList();

            return new
            {
                statusDistribution = statusData,
                monthlyTrends = monthlyData,
                departmentDistribution = departmentData,
                dateRange = $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}"
            };
        }
    }
}