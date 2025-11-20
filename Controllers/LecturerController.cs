using ContractMonthlyClaimSystem.Data;
using ContractMonthlyClaimSystem.Models;
using ContractMonthlyClaimSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ContractMonthlyClaimSystem.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class LecturerController : Controller
    {
        private readonly IClaimService _claimService;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;

        public LecturerController(
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

        public async Task<IActionResult> Dashboard()
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null) return RedirectToAction("Logout", "Account");

            // Dashboard statistics
            ViewBag.PendingClaimsCount = await _context.Claims
                .CountAsync(c => c.LecturerID == lecturer.LecturerID && c.Status == "Pending");

            ViewBag.ApprovedClaimsCount = await _context.Claims
                .CountAsync(c => c.LecturerID == lecturer.LecturerID && c.Status == "Approved");

            ViewBag.RejectedClaimsCount = await _context.Claims
                .CountAsync(c => c.LecturerID == lecturer.LecturerID && c.Status == "Rejected");

            ViewBag.TotalClaimsCount = await _context.Claims
                .CountAsync(c => c.LecturerID == lecturer.LecturerID);

            // Recent claims for dashboard
            ViewBag.RecentClaims = await _context.Claims
                .Where(c => c.LecturerID == lecturer.LecturerID)
                .OrderByDescending(c => c.SubmissionDate)
                .Take(5)
                .ToListAsync();

            ViewBag.LecturerName = lecturer.Name;
            ViewBag.HourlyRate = lecturer.HourlyRate;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> SubmitClaim()
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null) return RedirectToAction("Logout", "Account");

            ViewBag.HourlyRate = lecturer.HourlyRate;
            return View(new Claim());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitClaim(Claim claim, IFormFile supportingDocument)
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null) return RedirectToAction("Logout", "Account");

            if (ModelState.IsValid)
            {
                try
                {
                    claim.LecturerID = lecturer.LecturerID;
                    var submittedClaim = await _claimService.SubmitClaimAsync(claim);

                    if (supportingDocument != null && supportingDocument.Length > 0)
                    {
                        await _claimService.UploadDocumentAsync(submittedClaim.ClaimID, supportingDocument);
                    }

                    TempData["SuccessMessage"] = "Claim submitted successfully!";
                    return RedirectToAction("TrackStatus");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "Error submitting claim: " + ex.Message);
                }
            }

            ViewBag.HourlyRate = lecturer.HourlyRate;
            return View(claim);
        }

        [HttpGet]
        public async Task<IActionResult> UploadDocuments(int? claimId)
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null) return RedirectToAction("Logout", "Account");

            // If claimId is provided, pre-select that claim
            if (claimId.HasValue)
            {
                var claim = await _context.Claims
                    .FirstOrDefaultAsync(c => c.ClaimID == claimId && c.LecturerID == lecturer.LecturerID);

                if (claim != null)
                {
                    ViewBag.SelectedClaimId = claimId;
                    ViewBag.SelectedClaimReference = $"Claim #{claim.ClaimID} - {claim.SubmissionDate:dd/MM/yyyy}";
                }
            }

            // Get all claims for dropdown
            var claims = await _context.Claims
                .Where(c => c.LecturerID == lecturer.LecturerID)
                .OrderByDescending(c => c.SubmissionDate)
                .ToListAsync();

            ViewBag.Claims = claims;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UploadDocuments(int claimId, IFormFile file)
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null) return Json(new { success = false, message = "User not found" });

            var claim = await _context.Claims.FindAsync(claimId);
            if (claim == null || claim.LecturerID != lecturer.LecturerID)
                return Json(new { success = false, message = "Claim not found or access denied" });

            if (file == null || file.Length == 0)
            {
                return Json(new { success = false, message = "Please select a file to upload." });
            }

            var success = await _claimService.UploadDocumentAsync(claimId, file);

            if (success)
            {
                return Json(new { success = true, message = "Document uploaded successfully!" });
            }
            else
            {
                return Json(new { success = false, message = "Failed to upload document. Please check file type and size." });
            }
        }
        [HttpGet]
        public async Task<IActionResult> TrackStatus()
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null) return RedirectToAction("Logout", "Account");

            var claims = await _context.Claims
                .Include(c => c.SupportingDocuments)
                .Include(c => c.AuditTrails)
                .ThenInclude(a => a.User)
                .Where(c => c.LecturerID == lecturer.LecturerID)
                .OrderByDescending(c => c.SubmissionDate)
                .ToListAsync();

            // Group claims by status for better organization
            ViewBag.PendingClaims = claims.Where(c => c.Status == "Pending" || c.Status == "Under Review").ToList();
            ViewBag.ApprovedClaims = claims.Where(c => c.Status == "Approved").ToList();
            ViewBag.RejectedClaims = claims.Where(c => c.Status == "Rejected").ToList();

            return View(claims);
        }

        [HttpGet]
        public async Task<IActionResult> History()
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null) return RedirectToAction("Logout", "Account");

            var claims = await _context.Claims
                .Include(c => c.SupportingDocuments)
                .Include(c => c.AuditTrails)
                .ThenInclude(a => a.User)
                .Where(c => c.LecturerID == lecturer.LecturerID)
                .OrderByDescending(c => c.SubmissionDate)
                .ToListAsync();

            return View(claims);
        }

        [HttpGet]
        public async Task<IActionResult> ClaimDetails(int id)
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null) return RedirectToAction("Logout", "Account");

            var claim = await _context.Claims
                .Include(c => c.SupportingDocuments)
                .Include(c => c.AuditTrails)
                .ThenInclude(a => a.User)
                .FirstOrDefaultAsync(c => c.ClaimID == id && c.LecturerID == lecturer.LecturerID);

            if (claim == null)
            {
                return NotFound();
            }

            return View(claim);
        }

        [HttpPost]
        public async Task<IActionResult> DeleteDocument(int documentId)
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null) return Json(new { success = false, message = "User not found" });

            var document = await _context.SupportingDocuments
                .Include(d => d.Claim)
                .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.Claim.LecturerID == lecturer.LecturerID);

            if (document == null)
            {
                return Json(new { success = false, message = "Document not found or access denied" });
            }

            try
            {
                // Delete physical file
                var filePath = Path.Combine(_environment.WebRootPath, "documents", document.FilePath);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }

                // Delete database record
                _context.SupportingDocuments.Remove(document);
                await _context.SaveChangesAsync();

                await _claimService.LogAuditAsync(document.ClaimID, lecturer.LecturerID.ToString(), $"Document Deleted: {document.FileName}");

                return Json(new { success = true, message = "Document deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error deleting document: " + ex.Message });
            }
        }

        private async Task<Lecturer> GetCurrentLecturerAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return null;

            return await _context.Lecturers
                .FirstOrDefaultAsync(l => l.UserId == user.Id);
        }

        [HttpGet]
        public async Task<IActionResult> GetClaimDocuments(int claimId)
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null) return Content("<p class='text-danger'>User not found</p>");

            var claim = await _context.Claims
                .Include(c => c.SupportingDocuments)
                .FirstOrDefaultAsync(c => c.ClaimID == claimId && c.LecturerID == lecturer.LecturerID);

            if (claim == null)
            {
                return Content("<p class='text-danger'>Claim not found or access denied</p>");
            }

            var documents = claim.SupportingDocuments.OrderByDescending(d => d.UploadedDate).ToList();

            if (!documents.Any())
            {
                return Content("<p class='text-muted'>No documents uploaded for this claim yet.</p>");
            }

            var html = new System.Text.StringBuilder();
            html.Append("<div class='list-group'>");

            foreach (var doc in documents)
            {
                html.Append($@"
            <div class='list-group-item d-flex justify-content-between align-items-center'>
                <div>
                    <i class='fas fa-file-{GetFileIcon(doc.FileName)} text-primary me-2'></i>
                    <span>{doc.FileName}</span>
                    <small class='text-muted d-block'>Uploaded: {doc.UploadedDate:dd MMM yyyy HH:mm}</small>
                </div>
                <div>
                    <button class='btn btn-sm btn-outline-danger' onclick='deleteDocument({doc.DocumentID})'>
                        <i class='fas fa-trash'></i>
                    </button>
                </div>
            </div>");
            }

            html.Append("</div>");

            return Content(html.ToString());
        }

        private string GetFileIcon(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".pdf" => "pdf",
                ".docx" => "word",
                ".xlsx" => "excel",
                ".jpg" or ".jpeg" or ".png" => "image",
                _ => "alt"
            };
        }

        

        

        [HttpGet]
        public async Task<IActionResult> GetClaimTimeline(int claimId)
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null) return Json(new { success = false, message = "User not found" });

            var claim = await _context.Claims
                .Include(c => c.AuditTrails)
                .ThenInclude(a => a.User)
                .FirstOrDefaultAsync(c => c.ClaimID == claimId && c.LecturerID == lecturer.LecturerID);

            if (claim == null)
            {
                return Json(new { success = false, message = "Claim not found" });
            }

            var timeline = claim.AuditTrails
                .OrderBy(a => a.Timestamp)
                .Select(a => new
                {
                    date = a.Timestamp.ToString("dd MMM yyyy HH:mm"),
                    action = a.Action,
                    user = a.User?.FullName ?? "System",
                    icon = GetTimelineIcon(a.Action)
                })
                .ToList();

            // Add submission as first timeline item
            timeline.Insert(0, new
            {
                date = claim.SubmissionDate.ToString("dd MMM yyyy HH:mm"),
                action = "Claim Submitted",
                user = "You",
                icon = "fas fa-paper-plane"
            });

            return Json(new { success = true, timeline });
        }

        private string GetTimelineIcon(string action)
        {
            return action.ToLower() switch
            {
                var a when a.Contains("submitted") => "fas fa-paper-plane",
                var a when a.Contains("approved") => "fas fa-check-circle",
                var a when a.Contains("rejected") => "fas fa-times-circle",
                var a when a.Contains("uploaded") => "fas fa-file-upload",
                var a when a.Contains("verified") => "fas fa-check-double",
                var a when a.Contains("review") => "fas fa-search",
                _ => "fas fa-info-circle"
            };
        }
    }
}