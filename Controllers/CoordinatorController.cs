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
// Add this alias to resolve the naming conflict
using MyClaim = ContractMonthlyClaimSystem.Models.Claim;

namespace ContractMonthlyClaimSystem.Controllers
{
    [Authorize(Roles = "Coordinator")]
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
            var user = await _userManager.GetUserAsync(User);
            var pendingClaims = await _claimService.GetPendingClaimsAsync();

            ViewBag.UserName = user?.FullName;
            ViewBag.UserRole = User.IsInRole("Manager") ? "Academic Manager" : "Programme Coordinator";
            ViewBag.PendingClaimsCount = pendingClaims.Count;
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

            return View(pendingClaims);
        }


        // Review Claims - List View
        [HttpGet]
        public async Task<IActionResult> ReviewClaims(string status = "Pending")
        {
            var claims = await GetClaimsByStatusAsync(status);
            ViewBag.CurrentStatus = status;
            ViewBag.StatusList = new List<string> { "Pending", "Under Review", "Coordinator Approved", "Rejected", "Revision Requested" };

            return View(claims);
        }

        // Review Claim - Detail View
        [HttpGet]
        public async Task<IActionResult> ReviewClaim(int id)
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
                return NotFound();
            }

            return View(claim);
        }

        // Forward to Manager (Approve)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForwardToManager(int claimId, string notes)
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

            return RedirectToAction("ReviewClaims");
        }

        // Request Revision
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestRevision(int claimId, string revisionReason)
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

            return RedirectToAction("ReviewClaims");
        }

        // Reject Claim
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectClaim(int claimId, string reason)
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

            return RedirectToAction("ReviewClaims");
        }

        // Set Under Review
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetUnderReview(int claimId)
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

            return RedirectToAction("ReviewClaims");
        }

        // Verify Documents
        [HttpGet]
        public async Task<IActionResult> VerifyDocuments(int? claimId, string status = "All")
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

        // Verify Single Document
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyDocument(int documentId, bool isValid, string notes)
        {
            var document = await _context.SupportingDocuments
                .Include(d => d.Claim)
                .FirstOrDefaultAsync(d => d.DocumentID == documentId);

            if (document != null)
            {
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

            return RedirectToAction("VerifyDocuments", new { claimId = document?.ClaimID });
        }

        // Batch Verify Documents
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BatchVerifyDocuments(int claimId, string documentActions)
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
                            var notes = actionParts.Length > 2 ? actionParts[2] : "";
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

                TempData["SuccessMessage"] = "Documents verified successfully!";
            }
            else
            {
                TempData["ErrorMessage"] = "Claim not found.";
            }

            return RedirectToAction("VerifyDocuments", new { claimId = claimId });
        }

        // Generate Reports
        [HttpGet]
        public async Task<IActionResult> GenerateReports(string reportType = "Monthly", DateTime? startDate = null, DateTime? endDate = null, string department = "All")
        {
            // Set default date range to current month if not provided
            if (!startDate.HasValue || !endDate.HasValue)
            {
                startDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
                endDate = startDate.Value.AddMonths(1).AddDays(-1);
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

            ViewBag.ReportTypes = new List<string> { "Monthly", "Weekly", "Quarterly", "Custom" };
            ViewBag.Departments = await GetDepartmentsListAsync();

            return View(reportData);
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
        [HttpGet]
        public async Task<IActionResult> ExportReport(string reportType, DateTime startDate, DateTime endDate, string department = "All", string format = "PDF")
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

            if (format == "PDF")
            {
                return View("CoordinatorReportPDF", reportData);
            }
            else
            {
                return View("CoordinatorReportExcel", reportData);
            }
        }

        // Download Document
        [HttpGet]
        public async Task<IActionResult> DownloadDocument(int documentId)
        {
            var document = await _context.SupportingDocuments.FindAsync(documentId);
            if (document == null)
            {
                return NotFound();
            }

            var filePath = Path.Combine(_environment.WebRootPath, "documents", document.FilePath);
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound();
            }

            var memory = new MemoryStream();
            using (var stream = new FileStream(filePath, FileMode.Open))
            {
                await stream.CopyToAsync(memory);
            }
            memory.Position = 0;

            return File(memory, GetContentType(document.FilePath), document.FileName);
        }

        // Quick Actions
        [HttpPost]
        public async Task<IActionResult> QuickApprove(int claimId)
        {
            var user = await _userManager.GetUserAsync(User);
            var success = await _claimService.ApproveClaimAsync(claimId, user.Id);

            if (success)
            {
                TempData["SuccessMessage"] = "Claim approved successfully!";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to approve claim.";
            }

            return RedirectToAction("ReviewClaims");
        }

        [HttpPost]
        public async Task<IActionResult> QuickReject(int claimId)
        {
            var user = await _userManager.GetUserAsync(User);
            var success = await _claimService.RejectClaimAsync(claimId, user.Id, "Quick rejection by coordinator");

            if (success)
            {
                TempData["SuccessMessage"] = "Claim rejected successfully!";
            }
            else
            {
                TempData["ErrorMessage"] = "Failed to reject claim.";
            }

            return RedirectToAction("ReviewClaims");
        }

        // Statistics API
        [HttpGet]
        public async Task<IActionResult> GetStatistics()
        {
            var statistics = new
            {
                TotalClaims = await _context.Claims.CountAsync(),
                PendingClaims = await _context.Claims.CountAsync(c => c.Status == "Pending"),
                ApprovedClaims = await _context.Claims.CountAsync(c => c.Status == "Approved" || c.Status == "Coordinator Approved"),
                RejectedClaims = await _context.Claims.CountAsync(c => c.Status == "Rejected"),
                TotalAmount = await _context.Claims.Where(c => c.Status == "Approved" || c.Status == "Coordinator Approved").SumAsync(c => c.Amount)
            };

            return Json(statistics);
        }

        // Helper Methods
        private async Task<List<MyClaim>> GetClaimsByStatusAsync(string status)
        {
            return await _context.Claims
                .Include(c => c.Lecturer)
                .ThenInclude(l => l.User)
                .Include(c => c.SupportingDocuments)
                .Include(c => c.AuditTrails)
                .Where(c => c.Status == status)
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
                .Select(l => l.Department)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync();

            departments.Insert(0, "All");
            return departments;
        }

        private async Task<double> CalculateAverageProcessingTime(List<MyClaim> claims)
        {
            var processedClaims = claims.Where(c => c.Status == "Approved" || c.Status == "Coordinator Approved" || c.Status == "Rejected");

            if (!processedClaims.Any())
                return 0;

            double totalDays = 0;
            int count = 0;

            foreach (var claim in processedClaims)
            {
                var approvalAudit = claim.AuditTrails?
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
                { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
                { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
                { ".jpg", "image/jpeg" },
                { ".jpeg", "image/jpeg" },
                { ".png", "image/png" }
            };

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return types.ContainsKey(ext) ? types[ext] : "application/octet-stream";
        }

        // Request Document Revision
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestDocumentRevision(int claimId, int documentId, string revisionReason)
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

            return RedirectToAction("ReviewClaim", new { id = claimId });
        }
    }
}