using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ContractMonthlyClaimSystem.Models;
using ContractMonthlyClaimSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using ContractMonthlyClaimSystem.Data;
using ContractMonthlyClaimSystem.Models.ViewModels;
using System.Globalization;

namespace ContractMonthlyClaimSystem.Controllers
{
    [Authorize(Roles = "HR")]
    public class HRController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IReportService _reportService;
        private readonly IWebHostEnvironment _environment;

        public HRController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IReportService reportService,
            IWebHostEnvironment environment)
        {
            _context = context;
            _userManager = userManager;
            _reportService = reportService;
            _environment = environment;
        }

        // HR Dashboard
        public async Task<IActionResult> Dashboard()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            // Dashboard statistics
            ViewBag.TotalLecturers = await _context.Lecturers.CountAsync();
            ViewBag.ActiveLecturers = await _context.Lecturers
                .CountAsync(l => l.User.IsActive);

            ViewBag.PendingPayments = await _context.Claims
                .CountAsync(c => c.Status == "Approved" && !c.IsPaid);

            var now = DateTime.Now;
            ViewBag.TotalPaymentsThisMonth = await _context.Claims
                .Where(c => c.Status == "Approved" && c.PaymentDate.HasValue &&
                           c.PaymentDate.Value.Month == now.Month &&
                           c.PaymentDate.Value.Year == now.Year)
                .SumAsync(c => c.Amount);

            ViewBag.UserName = user.FullName ?? "HR Manager";

            return View();
        }

        // Lecturer Management
        [HttpGet]
        public async Task<IActionResult> ManageLecturers(string search = "", string department = "All")
        {
            var query = _context.Lecturers
                .Include(l => l.User)
                .Include(l => l.Claims)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(l =>
                    l.Name.Contains(search) ||
                    l.User.Email.Contains(search) ||
                    l.User.EmployeeId.Contains(search));
            }

            if (department != "All")
            {
                query = query.Where(l => l.Department == department);
            }

            var lecturers = await query
                .OrderBy(l => l.Name)
                .ToListAsync();

            ViewBag.SearchTerm = search;
            ViewBag.Departments = await GetDepartmentsListAsync();
            ViewBag.SelectedDepartment = department;

            return View(lecturers);
        }

        // Lecturer Details
        [HttpGet]
        public async Task<IActionResult> LecturerDetails(int id)
        {
            var lecturer = await _context.Lecturers
                .Include(l => l.User)
                .Include(l => l.Claims)
                .ThenInclude(c => c.AuditTrails)
                .FirstOrDefaultAsync(l => l.LecturerID == id);

            if (lecturer == null)
            {
                return NotFound();
            }

            return View(lecturer);
        }

        // Edit Lecturer
        [HttpGet]
        public async Task<IActionResult> EditLecturer(int id)
        {
            var lecturer = await _context.Lecturers
                .Include(l => l.User)
                .FirstOrDefaultAsync(l => l.LecturerID == id);

            if (lecturer == null || lecturer.User == null)
            {
                return NotFound();
            }

            var model = new EditLecturerViewModel
            {
                LecturerID = lecturer.LecturerID,
                Name = lecturer.Name,
                Email = lecturer.User.Email ?? string.Empty,
                Department = lecturer.Department,
                HourlyRate = lecturer.HourlyRate,
                EmployeeId = lecturer.User.EmployeeId ?? string.Empty,
                PhoneNumber = lecturer.User.PhoneNumber ?? string.Empty
            };

            ViewBag.Departments = await GetDepartmentsListAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditLecturer(EditLecturerViewModel model)
        {
            if (ModelState.IsValid)
            {
                var lecturer = await _context.Lecturers
                    .Include(l => l.User)
                    .FirstOrDefaultAsync(l => l.LecturerID == model.LecturerID);

                if (lecturer != null && lecturer.User != null)
                {
                    lecturer.Name = model.Name;
                    lecturer.Department = model.Department;
                    lecturer.HourlyRate = model.HourlyRate;

                    lecturer.User.Email = model.Email;
                    lecturer.User.PhoneNumber = model.PhoneNumber;
                    lecturer.User.EmployeeId = model.EmployeeId;

                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = "Lecturer information updated successfully!";
                    return RedirectToAction("LecturerDetails", new { id = model.LecturerID });
                }

                ModelState.AddModelError("", "Lecturer not found.");
            }

            ViewBag.Departments = await GetDepartmentsListAsync();
            return View(model);
        }

        // Payment Processing
        [HttpGet]
        public async Task<IActionResult> ProcessPayments(string period = "current")
        {
            DateTime startDate, endDate;
            var now = DateTime.Now;

            switch (period)
            {
                case "previous":
                    var prevMonth = now.AddMonths(-1);
                    startDate = new DateTime(prevMonth.Year, prevMonth.Month, 1);
                    endDate = startDate.AddMonths(1).AddDays(-1);
                    break;
                case "current":
                default:
                    startDate = new DateTime(now.Year, now.Month, 1);
                    endDate = startDate.AddMonths(1).AddDays(-1);
                    break;
            }

            var approvedClaims = await _context.Claims
                .Include(c => c.Lecturer)
                .ThenInclude(l => l.User)
                .Where(c => c.Status == "Approved" &&
                           !c.IsPaid &&
                           c.SubmissionDate >= startDate &&
                           c.SubmissionDate <= endDate)
                .OrderBy(c => c.Lecturer != null ? c.Lecturer.Name : "")
                .ToListAsync();

            var paymentBatch = new PaymentBatchViewModel
            {
                Period = period,
                StartDate = startDate,
                EndDate = endDate,
                Claims = approvedClaims ?? new List<Claim>(),
                SelectedClaimIds = new List<int>(),
                TotalAmount = approvedClaims?.Sum(c => c.Amount) ?? 0,
                TotalClaims = approvedClaims?.Count ?? 0
            };

            ViewBag.Periods = new List<string> { "current", "previous" };
            return View(paymentBatch);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GeneratePaymentBatch(PaymentBatchViewModel model)
        {
            if (model.SelectedClaimIds == null || !model.SelectedClaimIds.Any())
            {
                TempData["ErrorMessage"] = "No claims selected for payment.";
                return RedirectToAction("ProcessPayments");
            }

            try
            {
                var claims = await _context.Claims
                    .Include(c => c.Lecturer)
                    .Where(c => model.SelectedClaimIds.Contains(c.ClaimID))
                    .ToListAsync();

                if (!claims.Any())
                {
                    TempData["ErrorMessage"] = "No valid claims found for the selected IDs.";
                    return RedirectToAction("ProcessPayments");
                }

                // Generate payment batch
                var paymentBatch = new PaymentBatch
                {
                    BatchNumber = GenerateBatchNumber(),
                    GeneratedDate = DateTime.Now,
                    TotalAmount = claims.Sum(c => c.Amount),
                    TotalClaims = claims.Count,
                    GeneratedBy = User.Identity?.Name ?? "System"
                };

                _context.PaymentBatches.Add(paymentBatch);
                await _context.SaveChangesAsync();

                // Mark claims as paid
                foreach (var claim in claims)
                {
                    claim.IsPaid = true;
                    claim.PaymentDate = DateTime.Now;
                    claim.PaymentBatchId = paymentBatch.BatchId;
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Payment batch {paymentBatch.BatchNumber} generated successfully for {claims.Count} claims!";
                return RedirectToAction("PaymentBatchDetails", new { id = paymentBatch.BatchId });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error generating payment batch: {ex.Message}";
                return RedirectToAction("ProcessPayments");
            }
        }

        // Payment Batch Details
        [HttpGet]
        public async Task<IActionResult> PaymentBatchDetails(int id)
        {
            var paymentBatch = await _context.PaymentBatches
                .Include(pb => pb.Claims)
                .ThenInclude(c => c.Lecturer)
                .FirstOrDefaultAsync(pb => pb.BatchId == id);

            if (paymentBatch == null)
            {
                return NotFound();
            }

            return View(paymentBatch);
        }

        // Generate Invoices
        [HttpGet]
        public async Task<IActionResult> GenerateInvoices(string reportType = "Monthly", DateTime? startDate = null, DateTime? endDate = null)
        {
            if (!startDate.HasValue || !endDate.HasValue)
            {
                var now = DateTime.Now;
                startDate = new DateTime(now.Year, now.Month, 1);
                endDate = startDate.Value.AddMonths(1).AddDays(-1);
            }

            var claims = await _context.Claims
                .Include(c => c.Lecturer)
                .Where(c => c.Status == "Approved" &&
                           c.SubmissionDate >= startDate &&
                           c.SubmissionDate <= endDate)
                .OrderBy(c => c.Lecturer != null ? c.Lecturer.Department : "")
                .ThenBy(c => c.Lecturer != null ? c.Lecturer.Name : "")
                .ToListAsync();

            var invoiceData = new InvoiceReportViewModel
            {
                ReportType = reportType,
                StartDate = startDate.Value,
                EndDate = endDate.Value,
                Claims = claims ?? new List<Claim>(),
                TotalAmount = claims?.Sum(c => c.Amount) ?? 0,
                TotalClaims = claims?.Count ?? 0
            };

            ViewBag.ReportTypes = new List<string> { "Monthly", "Weekly", "Quarterly", "Custom" };
            return View(invoiceData);
        }

        [HttpPost]
        public async Task<IActionResult> DownloadInvoiceReport(InvoiceReportViewModel model, string format = "PDF")
        {
            var claims = await _context.Claims
                .Include(c => c.Lecturer)
                .Where(c => c.Status == "Approved" &&
                           c.SubmissionDate >= model.StartDate &&
                           c.SubmissionDate <= model.EndDate)
                .ToListAsync();

            if (!claims.Any())
            {
                TempData["ErrorMessage"] = "No claims found for the selected period.";
                return RedirectToAction("GenerateInvoices");
            }

            try
            {
                if (format == "PDF")
                {
                    var pdfBytes = await _reportService.GenerateInvoiceReportPdf(claims, model.StartDate, model.EndDate);
                    return File(pdfBytes, "application/pdf",
                        $"Invoices_{model.StartDate:yyyyMMdd}_{model.EndDate:yyyyMMdd}.pdf");
                }
                else // Excel
                {
                    var excelBytes = await _reportService.GenerateInvoiceReportExcel(claims, model.StartDate, model.EndDate);
                    return File(excelBytes,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"Invoices_{model.StartDate:yyyyMMdd}_{model.EndDate:yyyyMMdd}.xlsx");
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error generating report: {ex.Message}";
                return RedirectToAction("GenerateInvoices");
            }
        }

        // Automated Reports
        [HttpGet]
        public async Task<IActionResult> AutomatedReports()
        {
            var scheduledReports = await _context.ScheduledReports
                .OrderBy(sr => sr.NextRunDate)
                .ToListAsync();

            return View(scheduledReports ?? new List<ScheduledReport>());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScheduleReport(ScheduledReport model)
        {
            if (ModelState.IsValid)
            {
                model.IsActive = true;
                model.CreatedDate = DateTime.Now;
                model.NextRunDate = CalculateNextRunDate(model.Frequency, model.ScheduleTime);

                _context.ScheduledReports.Add(model);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Report scheduled successfully!";
            }
            else
            {
                TempData["ErrorMessage"] = "Invalid report schedule data.";
            }

            return RedirectToAction("AutomatedReports");
        }

        // Bulk Operations
        [HttpGet]
        public IActionResult BulkOperations()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkUpdateHourlyRates(BulkUpdateViewModel model)
        {
            if (ModelState.IsValid)
            {
                var lecturers = await _context.Lecturers
                    .Where(l => model.Department == "All" || l.Department == model.Department)
                    .ToListAsync();

                foreach (var lecturer in lecturers)
                {
                    if (model.UpdateType == "Fixed")
                    {
                        lecturer.HourlyRate = model.NewValue;
                    }
                    else if (model.UpdateType == "Percentage")
                    {
                        lecturer.HourlyRate *= (1 + model.NewValue / 100);
                    }
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Hourly rates updated for {lecturers.Count} lecturers!";
            }
            else
            {
                TempData["ErrorMessage"] = "Invalid bulk update data.";
            }

            return RedirectToAction("BulkOperations");
        }

        // HR Analytics
        [HttpGet]
        public async Task<IActionResult> Analytics()
        {
            var analytics = new HRAnalyticsViewModel
            {
                TotalLecturers = await _context.Lecturers.CountAsync(),
                ActiveLecturers = await _context.Lecturers.CountAsync(l => l.User.IsActive),
                TotalClaimsProcessed = await _context.Claims.CountAsync(c => c.Status == "Approved"),
                TotalPayments = await _context.Claims.Where(c => c.IsPaid).SumAsync(c => c.Amount),
                DepartmentStats = await GetDepartmentStatisticsAsync(),
                MonthlyTrends = await GetMonthlyTrendsAsync()
            };

            return View(analytics);
        }

        // Helper Methods
        private async Task<List<string>> GetDepartmentsListAsync()
        {
            var departments = await _context.Lecturers
                .Where(l => l.Department != null)
                .Select(l => l.Department!)
                .Distinct()
                .OrderBy(d => d)
                .ToListAsync();

            departments.Insert(0, "All");
            return departments;
        }

        private string GenerateBatchNumber()
        {
            return $"BATCH-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
        }

        private DateTime CalculateNextRunDate(string frequency, TimeSpan scheduleTime)
        {
            var nextRun = DateTime.Today.Add(scheduleTime);

            switch (frequency)
            {
                case "Daily":
                    if (nextRun <= DateTime.Now) nextRun = nextRun.AddDays(1);
                    break;
                case "Weekly":
                    nextRun = nextRun.AddDays(7);
                    break;
                case "Monthly":
                    nextRun = nextRun.AddMonths(1);
                    break;
            }

            return nextRun;
        }

        private async Task<List<DepartmentStat>> GetDepartmentStatisticsAsync()
        {
            var stats = await _context.Claims
                .Include(c => c.Lecturer)
                .Where(c => c.Status == "Approved" && c.SubmissionDate.Year == DateTime.Now.Year)
                .GroupBy(c => c.Lecturer != null ? c.Lecturer.Department : "Unknown")
                .Select(g => new DepartmentStat
                {
                    Department = g.Key ?? "Unknown",
                    TotalClaims = g.Count(),
                    ApprovedClaims = g.Count(c => c.Status == "Approved"),
                    TotalAmount = g.Sum(c => c.Amount),
                    AverageAmount = g.Average(c => c.Amount)
                })
                .OrderByDescending(d => d.TotalAmount)
                .ToListAsync();

            return stats ?? new List<DepartmentStat>();
        }

        private async Task<List<MonthlyTrend>> GetMonthlyTrendsAsync()
        {
            var trends = new List<MonthlyTrend>();
            var now = DateTime.Now;

            for (int i = 11; i >= 0; i--)
            {
                var date = now.AddMonths(-i);
                var startDate = new DateTime(date.Year, date.Month, 1);
                var endDate = startDate.AddMonths(1).AddDays(-1);

                var monthlyData = await _context.Claims
                    .Where(c => c.SubmissionDate >= startDate && c.SubmissionDate <= endDate)
                    .GroupBy(c => 1)
                    .Select(g => new
                    {
                        TotalClaims = g.Count(),
                        ApprovedClaims = g.Count(c => c.Status == "Approved"),
                        TotalAmount = g.Where(c => c.Status == "Approved").Sum(c => c.Amount)
                    })
                    .FirstOrDefaultAsync();

                trends.Add(new MonthlyTrend
                {
                    Month = startDate.ToString("MMM yyyy"),
                    SubmittedClaims = monthlyData?.TotalClaims ?? 0,
                    ApprovedClaims = monthlyData?.ApprovedClaims ?? 0,
                    TotalAmount = monthlyData?.TotalAmount ?? 0
                });
            }

            return trends;
        }
    }
}