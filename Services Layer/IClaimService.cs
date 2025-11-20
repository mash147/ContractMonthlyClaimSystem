using ContractMonthlyClaimSystem.Data;
using ContractMonthlyClaimSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace ContractMonthlyClaimSystem.Services
{
    public interface IClaimService
    {
        Task<Claim> SubmitClaimAsync(Claim claim);
        Task<bool> UploadDocumentAsync(int claimId, IFormFile file);
        Task<List<Claim>> GetPendingClaimsAsync();
        Task<bool> ApproveClaimAsync(int claimId, string userId);
        Task<bool> RejectClaimAsync(int claimId, string userId, string reason);
        Task<Claim> GetClaimByIdAsync(int claimId);
        Task<List<Claim>> GetClaimsByLecturerAsync(int lecturerId);
        Task LogAuditAsync(int claimId, string userId, string action);
        Task<int> GetPendingClaimsCountAsync();
        Task<int> GetApprovedClaimsCountAsync();
        Task<int> GetTotalClaimsCountAsync();
        Task<List<Claim>> GetRecentClaimsByLecturerAsync(int lecturerId);
    }

    public class ClaimService : IClaimService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public ClaimService(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        public async Task<Claim> SubmitClaimAsync(Claim claim)
        {
            claim.SubmissionDate = DateTime.Now;
            claim.Status = "Pending";

            var lecturer = await _context.Lecturers.FindAsync(claim.LecturerID);
            if (lecturer != null)
            {
                claim.Amount = claim.HoursWorked * lecturer.HourlyRate;
            }

            _context.Claims.Add(claim);
            await _context.SaveChangesAsync();

            await LogAuditAsync(claim.ClaimID, claim.LecturerID.ToString(), "Claim Submitted");
            return claim;
        }

        public async Task<bool> UploadDocumentAsync(int claimId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return false;

            var allowedExtensions = new[] { ".pdf", ".docx", ".xlsx" };
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

            await LogAuditAsync(claimId, "system", $"Document Uploaded: {file.FileName}");
            return true;
        }

        public async Task<List<Claim>> GetPendingClaimsAsync()
        {
            return await _context.Claims
                .Include(c => c.Lecturer)
                .ThenInclude(l => l.User)
                .Include(c => c.SupportingDocuments)
                .Where(c => c.Status == "Pending")
                .OrderByDescending(c => c.SubmissionDate)
                .ToListAsync();
        }

        public async Task<bool> ApproveClaimAsync(int claimId, string userId)
        {
            var claim = await _context.Claims.FindAsync(claimId);
            if (claim == null) return false;

            claim.Status = "Approved";
            await _context.SaveChangesAsync();

            await LogAuditAsync(claimId, userId, "Claim Approved");
            return true;
        }

        public async Task<bool> RejectClaimAsync(int claimId, string userId, string reason)
        {
            var claim = await _context.Claims.FindAsync(claimId);
            if (claim == null) return false;

            claim.Status = "Rejected";
            claim.AdditionalNotes += $"\nRejection Reason: {reason}";
            await _context.SaveChangesAsync();

            await LogAuditAsync(claimId, userId, $"Claim Rejected: {reason}");
            return true;
        }

        public async Task<Claim> GetClaimByIdAsync(int claimId)
        {
            return await _context.Claims
                .Include(c => c.Lecturer)
                .ThenInclude(l => l.User)
                .Include(c => c.SupportingDocuments)
                .Include(c => c.AuditTrails)
                .ThenInclude(at => at.User)
                .FirstOrDefaultAsync(c => c.ClaimID == claimId);
        }

        public async Task<List<Claim>> GetClaimsByLecturerAsync(int lecturerId)
        {
            return await _context.Claims
                .Include(c => c.SupportingDocuments)
                .Where(c => c.LecturerID == lecturerId)
                .OrderByDescending(c => c.SubmissionDate)
                .ToListAsync();
        }

        public async Task<List<Claim>> GetRecentClaimsByLecturerAsync(int lecturerId)
        {
            return await _context.Claims
                .Where(c => c.LecturerID == lecturerId)
                .OrderByDescending(c => c.SubmissionDate)
                .Take(5)
                .ToListAsync();
        }

        public async Task<int> GetPendingClaimsCountAsync()
        {
            return await _context.Claims.CountAsync(c => c.Status == "Pending");
        }

        public async Task<int> GetApprovedClaimsCountAsync()
        {
            return await _context.Claims.CountAsync(c => c.Status == "Approved");
        }

        public async Task<int> GetTotalClaimsCountAsync()
        {
            return await _context.Claims.CountAsync();
        }

        public async Task LogAuditAsync(int claimId, string userId, string action)
        {
            var audit = new AuditTrail
            {
                ClaimID = claimId,
                UserId = userId,
                Action = action,
                Timestamp = DateTime.Now
            };

            _context.AuditTrails.Add(audit);
            await _context.SaveChangesAsync();
        }
    }
}