using System.ComponentModel.DataAnnotations;
using System.Text;
using ContractMonthlyClaimSystem.Data;
using ContractMonthlyClaimSystem.Models;
using ContractMonthlyClaimSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;

namespace ContractMonthlyClaimSystem.Controllers
{
    [Authorize(Roles = "Lecturer")]
    public class LecturerController : Controller
    {
        private readonly IClaimService _claimService;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<LecturerController> _logger;

        public LecturerController(
            IClaimService claimService,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment,
            ILogger<LecturerController> logger) // Add ILogger
        {
            _claimService = claimService;
            _context = context;
            _userManager = userManager;
            _environment = environment;
            _logger = logger;
        }

        public async Task<IActionResult> Dashboard()
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    TempData["ErrorMessage"] = "Lecturer profile not found. Please contact administrator.";
                    return RedirectToAction("Logout", "Account");
                }

                // Dashboard statistics
                var pendingClaimsCount = await _context.Claims
                    .CountAsync(c => c.LecturerID == lecturer.LecturerID && c.Status == "Pending");

                var approvedClaimsCount = await _context.Claims
                    .CountAsync(c => c.LecturerID == lecturer.LecturerID && c.Status == "Approved");

                var rejectedClaimsCount = await _context.Claims
                    .CountAsync(c => c.LecturerID == lecturer.LecturerID && c.Status == "Rejected");

                var totalClaimsCount = await _context.Claims
                    .CountAsync(c => c.LecturerID == lecturer.LecturerID);

                // Recent claims for dashboard
                var recentClaims = await _context.Claims
                    .Where(c => c.LecturerID == lecturer.LecturerID)
                    .OrderByDescending(c => c.SubmissionDate)
                    .Take(5)
                    .ToListAsync();

                ViewBag.PendingClaimsCount = pendingClaimsCount;
                ViewBag.ApprovedClaimsCount = approvedClaimsCount;
                ViewBag.RejectedClaimsCount = rejectedClaimsCount;
                ViewBag.TotalClaimsCount = totalClaimsCount;
                ViewBag.RecentClaims = recentClaims;
                ViewBag.LecturerName = lecturer.Name;
                ViewBag.HourlyRate = lecturer.HourlyRate;

                return View();
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading dashboard: " + ex.Message;
                return View();
            }
        }

        [HttpGet]
        public async Task<IActionResult> SubmitClaim()
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null)
            {
                TempData["ErrorMessage"] = "Lecturer profile not found. Please contact administrator.";
                return RedirectToAction("Dashboard");
            }

            ViewBag.HourlyRate = lecturer.HourlyRate;

            // Create a new claim with default values
            var claim = new Claim
            {
                SubmissionDate = DateTime.Now,
                HoursWorked = 0,
                Amount = 0,
                Status = "Pending"
            };

            return View(claim);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitClaim(Claim claim, IFormFile supportingDocument)
        {
            var lecturer = await GetCurrentLecturerAsync();
            if (lecturer == null)
            {
                TempData["ErrorMessage"] = "Lecturer profile not found.";
                return RedirectToAction("Dashboard");
            }

            try
            {
                // Create a new claim with explicit values
                var newClaim = new Claim
                {
                    LecturerID = lecturer.LecturerID,
                    HoursWorked = claim.HoursWorked,
                    TotalHours = claim.HoursWorked, // Explicitly set TotalHours
                    Amount = claim.HoursWorked * lecturer.HourlyRate,
                    SubmissionDate = DateTime.Now,
                    Status = "Pending",
                    AdditionalNotes = claim.AdditionalNotes ?? string.Empty,
                    IsPaid = false,
                    PaymentDate = null,
                    PaymentBatchId = null
                };

                // Add to context and save
                _context.Claims.Add(newClaim);
                await _context.SaveChangesAsync();

                // Handle document upload if provided
                if (supportingDocument != null && supportingDocument.Length > 0)
                {
                    await UploadDocumentAsync(newClaim.ClaimID, supportingDocument);
                }

                // Log the submission
                await _claimService.LogAuditAsync(newClaim.ClaimID, lecturer.LecturerID.ToString(),
                    "Claim submitted successfully");

                TempData["SuccessMessage"] = $"Claim submitted successfully! Claim ID: #{newClaim.ClaimID}";
                return RedirectToAction("TrackStatus");
            }
            catch (DbUpdateException dbEx)
            {
                // Get the root cause of the error
                var innerException = GetInnerException(dbEx);

                if (innerException.Message.Contains("FK_Claims_Lecturers_LecturerID"))
                {
                    TempData["ErrorMessage"] = "Database error: Invalid lecturer reference. Please contact administrator.";
                }
                else if (innerException.Message.Contains("Cannot insert the value NULL"))
                {
                    TempData["ErrorMessage"] = "Database error: Missing required field. Please check all fields are filled correctly.";
                }
                else
                {
                    TempData["ErrorMessage"] = $"Database error: {innerException.Message}";
                }

                // Log the detailed error
                _logger.LogError(dbEx, "Database error while submitting claim for lecturer {LecturerID}", lecturer?.LecturerID);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Unexpected error: {ex.Message}";
                _logger.LogError(ex, "Unexpected error while submitting claim for lecturer {LecturerID}", lecturer?.LecturerID);
            }

            ViewBag.HourlyRate = lecturer.HourlyRate;
            return View(claim);
        }

        // Helper method to get the innermost exception
        private Exception GetInnerException(Exception ex)
        {
            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
            }
            return ex;
        }

        [HttpGet]
        public async Task<IActionResult> TrackStatus()
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    TempData["ErrorMessage"] = "Lecturer profile not found.";
                    return RedirectToAction("Dashboard");
                }

                // Get all claims for the lecturer
                var claims = await _context.Claims
                    .Include(c => c.SupportingDocuments)
                    .Include(c => c.AuditTrails)
                    .ThenInclude(a => a.User)
                    .Where(c => c.LecturerID == lecturer.LecturerID)
                    .OrderByDescending(c => c.SubmissionDate)
                    .ToListAsync();

                // Group claims by status for summary cards
                var pendingClaims = claims.Where(c => c.Status == "Pending" || c.Status == "Under Review" || c.Status == "Revision Requested").ToList();
                var approvedClaims = claims.Where(c => c.Status == "Approved" || c.Status == "Coordinator Approved").ToList();
                var rejectedClaims = claims.Where(c => c.Status == "Rejected").ToList();

                ViewBag.PendingClaims = pendingClaims;
                ViewBag.ApprovedClaims = approvedClaims;
                ViewBag.RejectedClaims = rejectedClaims;

                return View(claims);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading claim status: " + ex.Message;
                return View(new List<Claim>());
            }
        }


        [HttpGet]
        public async Task<IActionResult> History()
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    TempData["ErrorMessage"] = "Lecturer profile not found.";
                    return RedirectToAction("Dashboard");
                }

                var claims = await _context.Claims
                    .Include(c => c.SupportingDocuments)
                    .Include(c => c.AuditTrails)
                    .ThenInclude(a => a.User)
                    .Where(c => c.LecturerID == lecturer.LecturerID)
                    .OrderByDescending(c => c.SubmissionDate)
                    .ToListAsync();

                return View(claims);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading claim history: " + ex.Message;
                return View(new List<Claim>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportHistory(string format = "csv")
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                var claims = await _context.Claims
                    .Include(c => c.SupportingDocuments)
                    .Include(c => c.AuditTrails)
                    .Where(c => c.LecturerID == lecturer.LecturerID)
                    .OrderByDescending(c => c.SubmissionDate)
                    .ToListAsync();

                if (format.ToLower() == "csv")
                {
                    var csvContent = GenerateHistoryCsv(claims);
                    var bytes = Encoding.UTF8.GetBytes(csvContent);
                    return File(bytes, "text/csv", $"claim-history-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
                }
                else if (format.ToLower() == "excel")
                {
                    var excelBytes = await GenerateHistoryExcel(claims);
                    return File(excelBytes,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"claim-history-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");
                }
                else
                {
                    return Json(new { success = false, message = "Unsupported format" });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error exporting history: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetClaimDocumentsDropdown(int claimId)
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                var claim = await _context.Claims
                    .Include(c => c.SupportingDocuments)
                    .FirstOrDefaultAsync(c => c.ClaimID == claimId && c.LecturerID == lecturer.LecturerID);

                if (claim == null)
                {
                    return Json(new { success = false, message = "Claim not found" });
                }

                var documents = claim.SupportingDocuments
                    .OrderByDescending(d => d.UploadedDate)
                    .Select(d => new
                    {
                        id = d.DocumentID,
                        name = d.FileName,
                        icon = GetFileIcon(d.FileName),
                        url = Url.Action("DownloadDocument", new { documentId = d.DocumentID })
                    })
                    .ToList();

                return Json(new { success = true, documents });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Helper methods for export
        private string GenerateHistoryCsv(List<Claim> claims)
        {
            var csv = new StringBuilder();

            // Headers
            csv.AppendLine("ClaimID,SubmissionDate,HoursWorked,Amount,Status,LastActivity,DocumentsCount,AdditionalNotes");

            // Data rows
            foreach (var claim in claims)
            {
                var lastActivity = claim.AuditTrails?
                    .OrderByDescending(a => a.Timestamp)
                    .FirstOrDefault()?.Timestamp ?? claim.SubmissionDate;

                csv.AppendLine(
                    $"{claim.ClaimID}," +
                    $"\"{claim.SubmissionDate:yyyy-MM-dd}\"," +
                    $"{claim.HoursWorked}," +
                    $"{claim.Amount}," +
                    $"\"{claim.Status}\"," +
                    $"\"{lastActivity:yyyy-MM-dd HH:mm}\"," +
                    $"{claim.SupportingDocuments?.Count ?? 0}," +
                    $"\"{EscapeCsvField(claim.AdditionalNotes ?? "")}\""
                );
            }

            return csv.ToString();
        }

        private async Task<byte[]> GenerateHistoryExcel(List<Claim> claims)
        {
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Claim History");

            // Title
            worksheet.Cells[1, 1].Value = "CLAIM HISTORY REPORT";
            worksheet.Cells[1, 1].Style.Font.Bold = true;
            worksheet.Cells[1, 1].Style.Font.Size = 16;
            worksheet.Cells[1, 1, 1, 8].Merge = true;

            worksheet.Cells[2, 1].Value = $"Generated on: {DateTime.Now:dd MMMM yyyy 'at' HH:mm}";
            worksheet.Cells[2, 1, 2, 8].Merge = true;

            worksheet.Cells[3, 1].Value = $"Total Claims: {claims.Count}";
            worksheet.Cells[3, 1, 3, 8].Merge = true;

            // Headers
            var headers = new[] { "Claim ID", "Submission Date", "Hours Worked", "Amount", "Status", "Last Activity", "Documents", "Additional Notes" };
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[5, i + 1].Value = headers[i];
                worksheet.Cells[5, i + 1].Style.Font.Bold = true;
                worksheet.Cells[5, i + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                worksheet.Cells[5, i + 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
            }

            // Data
            int row = 6;
            foreach (var claim in claims)
            {
                var lastActivity = claim.AuditTrails?
                    .OrderByDescending(a => a.Timestamp)
                    .FirstOrDefault()?.Timestamp ?? claim.SubmissionDate;

                worksheet.Cells[row, 1].Value = claim.ClaimID;
                worksheet.Cells[row, 2].Value = claim.SubmissionDate.ToString("yyyy-MM-dd");
                worksheet.Cells[row, 3].Value = claim.HoursWorked;
                worksheet.Cells[row, 4].Value = claim.Amount;
                worksheet.Cells[row, 4].Style.Numberformat.Format = "#,##0.00";
                worksheet.Cells[row, 5].Value = claim.Status;
                worksheet.Cells[row, 6].Value = lastActivity.ToString("yyyy-MM-dd HH:mm");
                worksheet.Cells[row, 7].Value = claim.SupportingDocuments?.Count ?? 0;
                worksheet.Cells[row, 8].Value = claim.AdditionalNotes;
                row++;
            }

            // Auto-fit columns
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            return package.GetAsByteArray();
        }

        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;

            if (field.Contains('"') || field.Contains(',') || field.Contains('\n') || field.Contains('\r'))
            {
                return '"' + field.Replace("\"", "\"\"") + '"';
            }

            return field;
        }
        [HttpGet]
        public async Task<IActionResult> ClaimDetails(int id)
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    TempData["ErrorMessage"] = "Lecturer profile not found.";
                    return RedirectToAction("TrackStatus");
                }

                var claim = await _context.Claims
                    .Include(c => c.SupportingDocuments)
                    .Include(c => c.AuditTrails)
                    .ThenInclude(a => a.User)
                    .Include(c => c.Lecturer)
                    .FirstOrDefaultAsync(c => c.ClaimID == id && c.LecturerID == lecturer.LecturerID);

                if (claim == null)
                {
                    TempData["ErrorMessage"] = "Claim not found or you don't have permission to view it.";
                    return RedirectToAction("TrackStatus");
                }

                return View(claim);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading claim details: " + ex.Message;
                return RedirectToAction("TrackStatus");
            }
        }

        [HttpGet]
        public async Task<IActionResult> UploadDocuments(int? claimId)
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    TempData["ErrorMessage"] = "Lecturer profile not found.";
                    return RedirectToAction("Dashboard");
                }

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
                    else
                    {
                        TempData["ErrorMessage"] = "Claim not found or access denied.";
                    }
                }

                // Get all claims for dropdown (only pending/under review claims can have documents uploaded)
                var claims = await _context.Claims
                    .Where(c => c.LecturerID == lecturer.LecturerID &&
                               (c.Status == "Pending" || c.Status == "Under Review" || c.Status == "Revision Requested"))
                    .OrderByDescending(c => c.SubmissionDate)
                    .ToListAsync();

                ViewBag.Claims = claims;

                return View();
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error loading upload page: " + ex.Message;
                return RedirectToAction("Dashboard");
            }
        }

        [HttpPost]
        public async Task<IActionResult> UploadDocuments(int claimId, IFormFile file)
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                // Verify claim belongs to lecturer and is in a state that allows document upload
                var claim = await _context.Claims
                    .FirstOrDefaultAsync(c => c.ClaimID == claimId && c.LecturerID == lecturer.LecturerID);

                if (claim == null)
                {
                    return Json(new { success = false, message = "Claim not found or access denied" });
                }

                // Check if claim is in a state that allows document upload
                if (claim.Status != "Pending" && claim.Status != "Under Review" && claim.Status != "Revision Requested")
                {
                    return Json(new
                    {
                        success = false,
                        message = $"Cannot upload documents for claims with status: {claim.Status}. Only Pending, Under Review, or Revision Requested claims can have documents uploaded."
                    });
                }

                if (file == null || file.Length == 0)
                {
                    return Json(new { success = false, message = "Please select a file to upload." });
                }

                // Validate file type
                var allowedExtensions = new[] { ".pdf", ".docx", ".xlsx", ".jpg", ".jpeg", ".png" };
                var fileExtension = Path.GetExtension(file.FileName).ToLower();

                if (!allowedExtensions.Contains(fileExtension))
                {
                    return Json(new
                    {
                        success = false,
                        message = $"File type not allowed. Allowed types: {string.Join(", ", allowedExtensions)}"
                    });
                }

                // Validate file size (5MB limit)
                if (file.Length > 5 * 1024 * 1024)
                {
                    return Json(new
                    {
                        success = false,
                        message = "File size exceeds 5MB limit."
                    });
                }

                // Create uploads directory if it doesn't exist
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "documents");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                // Generate unique filename
                var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                // Save file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Create document record
                var document = new SupportingDocument
                {
                    ClaimID = claimId,
                    FileName = file.FileName,
                    FilePath = uniqueFileName,
                    UploadedDate = DateTime.Now
                };

                _context.SupportingDocuments.Add(document);
                await _context.SaveChangesAsync();

                // Log audit trail
                await _claimService.LogAuditAsync(claimId, lecturer.LecturerID.ToString(),
                    $"Document uploaded: {file.FileName}");

                return Json(new
                {
                    success = true,
                    message = $"Document '{file.FileName}' uploaded successfully!"
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = $"Error uploading document: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public async Task<IActionResult> GetClaimDocuments(int claimId)
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    return Content("<div class='alert alert-danger'><i class='fas fa-exclamation-circle me-2'></i>User not found</div>");
                }

                var claim = await _context.Claims
                    .Include(c => c.SupportingDocuments)
                    .FirstOrDefaultAsync(c => c.ClaimID == claimId && c.LecturerID == lecturer.LecturerID);

                if (claim == null)
                {
                    return Content("<div class='alert alert-danger'><i class='fas fa-exclamation-circle me-2'></i>Claim not found or access denied</div>");
                }

                var documents = claim.SupportingDocuments.OrderByDescending(d => d.UploadedDate).ToList();

                if (!documents.Any())
                {
                    return Content(@"
                <div class='text-center py-4'>
                    <i class='fas fa-file-upload fa-3x text-muted mb-3'></i>
                    <h5 class='text-muted'>No Documents Uploaded</h5>
                    <p class='text-muted'>No documents have been uploaded for this claim yet.</p>
                </div>");
                }

                var html = new System.Text.StringBuilder();
                html.Append(@"
            <div class='table-responsive'>
                <table class='table table-sm table-hover'>
                    <thead class='table-light'>
                        <tr>
                            <th>File Name</th>
                            <th>Type</th>
                            <th>Uploaded</th>
                            <th>Size</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>");

                foreach (var doc in documents)
                {
                    var fileInfo = new FileInfo(Path.Combine(_environment.WebRootPath, "documents", doc.FilePath));
                    var fileSize = fileInfo.Exists ? FormatFileSize(fileInfo.Length) : "Unknown";
                    var fileIcon = GetFileIcon(doc.FileName);

                    html.Append($@"
                        <tr>
                            <td>
                                <i class='fas fa-file-{fileIcon} text-primary me-2'></i>
                                <span class='file-name'>{doc.FileName}</span>
                            </td>
                            <td>
                                <span class='badge bg-secondary'>{Path.GetExtension(doc.FileName).ToUpper()}</span>
                            </td>
                            <td>
                                <small>{doc.UploadedDate:dd MMM yyyy HH:mm}</small>
                            </td>
                            <td>
                                <small class='text-muted'>{fileSize}</small>
                            </td>
                            <td>
                                <div class='btn-group btn-group-sm'>
                                    <a href='{Url.Action("DownloadDocument", new { documentId = doc.DocumentID })}' 
                                       class='btn btn-outline-primary' target='_blank' 
                                       data-bs-toggle='tooltip' title='Download'>
                                        <i class='fas fa-download'></i>
                                    </a>
                                    <button class='btn btn-outline-danger' 
                                            onclick='deleteDocument({doc.DocumentID})'
                                            data-bs-toggle='tooltip' title='Delete'>
                                        <i class='fas fa-trash'></i>
                                    </button>
                                </div>
                            </td>
                        </tr>");
                }

                html.Append(@"
                    </tbody>
                </table>
            </div>");

                // Add summary
                html.Append($@"
            <div class='mt-3 p-3 bg-light rounded'>
                <div class='row text-center'>
                    <div class='col-4'>
                        <div class='h5 mb-0'>{documents.Count}</div>
                        <small class='text-muted'>Total Files</small>
                    </div>
                    <div class='col-4'>
                        <div class='h5 mb-0'>{documents.GroupBy(d => Path.GetExtension(d.FileName)).Count()}</div>
                        <small class='text-muted'>File Types</small>
                    </div>
                    <div class='col-4'>
                        <div class='h5 mb-0'>{documents.Min(d => d.UploadedDate):dd MMM}</div>
                        <small class='text-muted'>First Upload</small>
                    </div>
                </div>
            </div>");

                return Content(html.ToString());
            }
            catch (Exception ex)
            {
                return Content($"<div class='alert alert-danger'><i class='fas fa-exclamation-circle me-2'></i>Error loading documents: {ex.Message}</div>");
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteDocument(int documentId)
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                var document = await _context.SupportingDocuments
                    .Include(d => d.Claim)
                    .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.Claim.LecturerID == lecturer.LecturerID);

                if (document == null)
                {
                    return Json(new { success = false, message = "Document not found or access denied" });
                }

                // Delete physical file
                var filePath = Path.Combine(_environment.WebRootPath, "documents", document.FilePath);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }

                // Delete database record
                _context.SupportingDocuments.Remove(document);
                await _context.SaveChangesAsync();

                // Log audit trail
                await _claimService.LogAuditAsync(document.ClaimID, lecturer.LecturerID.ToString(),
                    $"Document deleted: {document.FileName}");

                return Json(new { success = true, message = "Document deleted successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error deleting document: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadDocument(int documentId)
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    return NotFound();
                }

                var document = await _context.SupportingDocuments
                    .Include(d => d.Claim)
                    .FirstOrDefaultAsync(d => d.DocumentID == documentId && d.Claim.LecturerID == lecturer.LecturerID);

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
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error downloading document: {ex.Message}";
                return RedirectToAction("UploadDocuments");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetClaimTimeline(int claimId)
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

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

                // Add current status as last item if different from submission
                if (claim.Status != "Pending")
                {
                    timeline.Add(new
                    {
                        date = claim.AuditTrails?.OrderByDescending(a => a.Timestamp).FirstOrDefault()?.Timestamp.ToString("dd MMM yyyy HH:mm") ?? claim.SubmissionDate.ToString("dd MMM yyyy HH:mm"),
                        action = $"Status: {claim.Status}",
                        user = claim.Status == "Approved" ? "System" : "Coordinator/Manager",
                        icon = GetStatusIcon(claim.Status)
                    });
                }

                return Json(new { success = true, timeline });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ResubmitClaim(int claimId)
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                var originalClaim = await _context.Claims
                    .FirstOrDefaultAsync(c => c.ClaimID == claimId && c.LecturerID == lecturer.LecturerID);

                if (originalClaim == null)
                {
                    return Json(new { success = false, message = "Claim not found" });
                }

                if (originalClaim.Status != "Rejected")
                {
                    return Json(new { success = false, message = "Only rejected claims can be resubmitted" });
                }

                // Create a new claim based on the rejected one
                var newClaim = new Claim
                {
                    LecturerID = lecturer.LecturerID,
                    HoursWorked = originalClaim.HoursWorked,
                    Amount = originalClaim.Amount,
                    TotalHours = originalClaim.TotalHours,
                    SubmissionDate = DateTime.Now,
                    Status = "Pending",
                    AdditionalNotes = $"Resubmitted from claim #{originalClaim.ClaimID}. Original submission: {originalClaim.SubmissionDate:dd MMM yyyy}"
                };

                _context.Claims.Add(newClaim);
                await _context.SaveChangesAsync();

                // Copy supporting documents if any
                var originalDocuments = await _context.SupportingDocuments
                    .Where(d => d.ClaimID == originalClaim.ClaimID)
                    .ToListAsync();

                foreach (var doc in originalDocuments)
                {
                    var newDocument = new SupportingDocument
                    {
                        ClaimID = newClaim.ClaimID,
                        FileName = doc.FileName,
                        FilePath = doc.FilePath,
                        UploadedDate = DateTime.Now
                    };
                    _context.SupportingDocuments.Add(newDocument);
                }

                await _context.SaveChangesAsync();

                // Log audit trail
                await _claimService.LogAuditAsync(newClaim.ClaimID, lecturer.LecturerID.ToString(),
                    $"Claim resubmitted from rejected claim #{originalClaim.ClaimID}");

                return Json(new { success = true, message = "Claim resubmitted successfully!", newClaimId = newClaim.ClaimID });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error resubmitting claim: " + ex.Message });
            }
        }

        private async Task<Lecturer> GetCurrentLecturerAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return null;

            return await _context.Lecturers
                .FirstOrDefaultAsync(l => l.UserId == user.Id);
        }

        private async Task<bool> UploadDocumentAsync(int claimId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return false;

            var allowedExtensions = new[] { ".pdf", ".docx", ".xlsx", ".jpg", ".jpeg", ".png" };
            var fileExtension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(fileExtension))
                return false;

            if (file.Length > 5 * 1024 * 1024) // 5MB limit
                return false;

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "documents");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = Guid.NewGuid().ToString() + fileExtension;
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var document = new SupportingDocument
            {
                ClaimID = claimId,
                FileName = file.FileName,
                FilePath = uniqueFileName,
                UploadedDate = DateTime.Now
            };

            _context.SupportingDocuments.Add(document);
            await _context.SaveChangesAsync();

            await _claimService.LogAuditAsync(claimId, "system", $"Document Uploaded: {file.FileName}");
            return true;
        }

        private string GetFileIcon(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            return extension switch
            {
                ".pdf" => "pdf",
                ".doc" or ".docx" => "word",
                ".xls" or ".xlsx" => "excel",
                ".jpg" or ".jpeg" or ".png" or ".gif" => "image",
                ".zip" or ".rar" => "archive",
                _ => "alt"
            };
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
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
        { ".png", "image/png" },
        { ".gif", "image/gif" }
    };

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return types.ContainsKey(ext) ? types[ext] : "application/octet-stream";
        }

        private string GetTimelineIcon(string action)
        {
            return action?.ToLower() switch
            {
                var a when a.Contains("submitted") => "fas fa-paper-plane",
                var a when a.Contains("approved") => "fas fa-check-circle",
                var a when a.Contains("rejected") => "fas fa-times-circle",
                var a when a.Contains("uploaded") => "fas fa-file-upload",
                var a when a.Contains("verified") => "fas fa-check-double",
                var a when a.Contains("review") => "fas fa-search",
                var a when a.Contains("resubmitted") => "fas fa-redo",
                _ => "fas fa-info-circle"
            };
        }

        private string GetStatusIcon(string status)
        {
            return status?.ToLower() switch
            {
                "approved" or "coordinator approved" => "fas fa-check-circle",
                "rejected" => "fas fa-times-circle",
                "pending" or "under review" or "revision requested" => "fas fa-clock",
                _ => "fas fa-info-circle"
            };
        }


        [HttpGet]
        public async Task<IActionResult> GetClaimStatistics()
        {
            try
            {
                var lecturer = await GetCurrentLecturerAsync();
                if (lecturer == null)
                {
                    return Json(new { success = false, message = "Lecturer not found" });
                }

                var pendingCount = await _context.Claims
                    .CountAsync(c => c.LecturerID == lecturer.LecturerID && c.Status == "Pending");

                var approvedCount = await _context.Claims
                    .CountAsync(c => c.LecturerID == lecturer.LecturerID && c.Status == "Approved");

                return Json(new
                {
                    success = true,
                    pendingCount = pendingCount,
                    approvedCount = approvedCount
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }

            [HttpGet]
             async Task<IActionResult> GetUploadStatistics()
            {
                try
                {
                    var lecturer = await GetCurrentLecturerAsync();
                    if (lecturer == null)
                    {
                        return Json(new { success = false, message = "User not found" });
                    }

                    var totalDocuments = await _context.SupportingDocuments
                        .Include(d => d.Claim)
                        .Where(d => d.Claim.LecturerID == lecturer.LecturerID)
                        .CountAsync();

                    var claimsWithDocuments = await _context.Claims
                        .Where(c => c.LecturerID == lecturer.LecturerID && c.SupportingDocuments.Any())
                        .CountAsync();

                    var lastUpload = await _context.SupportingDocuments
                        .Include(d => d.Claim)
                        .Where(d => d.Claim.LecturerID == lecturer.LecturerID)
                        .OrderByDescending(d => d.UploadedDate)
                        .Select(d => d.UploadedDate)
                        .FirstOrDefaultAsync();

                    return Json(new
                    {
                        success = true,
                        totalDocuments = totalDocuments,
                        claimsWithDocuments = claimsWithDocuments,
                        lastUpload = lastUpload.ToString("dd MMM yyyy HH:mm")
                    });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = ex.Message });
                }
            }

            [HttpGet]
            async Task<IActionResult> CheckDatabase()
            {
                try
                {
                    var lecturer = await GetCurrentLecturerAsync();
                    if (lecturer == null)
                    {
                        return Json(new { success = false, message = "Lecturer not found" });
                    }

                    // Check if lecturer exists and has valid data
                    var lecturerInfo = new
                    {
                        LecturerID = lecturer.LecturerID,
                        Name = lecturer.Name,
                        HourlyRate = lecturer.HourlyRate,
                        UserId = lecturer.UserId,
                        HasUser = lecturer.User != null
                    };

                    // Check database connection and basic operations
                    var canConnect = await _context.Database.CanConnectAsync();
                    var claimsCount = await _context.Claims.CountAsync();

                    return Json(new
                    {
                        success = true,
                        lecturer = lecturerInfo,
                        database = new
                        {
                            canConnect = canConnect,
                            claimsCount = claimsCount
                        },
                        message = "Database check completed"
                    });
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = ex.Message, innerException = ex.InnerException?.Message });
                }
            }

            [HttpGet]
             async Task<IActionResult> ClaimDetails(int id)
            {
                try
                {
                    var lecturer = await GetCurrentLecturerAsync();
                    if (lecturer == null)
                    {
                        TempData["ErrorMessage"] = "Lecturer profile not found.";
                        return RedirectToAction("TrackStatus");
                    }

                    var claim = await _context.Claims
                        .Include(c => c.SupportingDocuments)
                        .Include(c => c.AuditTrails)
                        .ThenInclude(a => a.User)
                        .Include(c => c.Lecturer)
                        .Include(c => c.PaymentBatch)
                        .FirstOrDefaultAsync(c => c.ClaimID == id && c.LecturerID == lecturer.LecturerID);

                    if (claim == null)
                    {
                        TempData["ErrorMessage"] = "Claim not found or you don't have permission to view it.";
                        return RedirectToAction("TrackStatus");
                    }

                    return View(claim);
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = "Error loading claim details: " + ex.Message;
                    return RedirectToAction("TrackStatus");
                }
            }

        }
    }
}