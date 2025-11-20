using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ContractMonthlyClaimSystem.Models;
using Microsoft.AspNetCore.Authorization;
using ContractMonthlyClaimSystem.Data;

namespace ContractMonthlyClaimSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context,
            ILogger<AccountController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    FullName = model.FullName,
                    Role = model.Role,
                    Department = model.Department,
                    HourlyRate = model.Role == "Lecturer" ? model.HourlyRate : null,
                    EmployeeId = GenerateEmployeeId(model.Role)
                };

                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    if (!await _roleManager.RoleExistsAsync(model.Role))
                    {
                        await _roleManager.CreateAsync(new IdentityRole(model.Role));
                    }

                    await _userManager.AddToRoleAsync(user, model.Role);

                    if (model.Role == "Lecturer")
                    {
                        await CreateLecturerRecord(user, model);
                    }
                    else if (model.Role == "Coordinator")
                    {
                        await CreateCoordinatorRecord(user);
                    }
                    else if (model.Role == "Manager")
                    {
                        await CreateManagerRecord(user);
                    }

                    await _signInManager.SignInAsync(user, isPersistent: false);
                    _logger.LogInformation("User created a new account with password.");
                    
                    return RedirectToAction("Dashboard", GetDashboardController(model.Role));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            return View(model);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(
                    model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User logged in.");
                    
                    var user = await _userManager.FindByEmailAsync(model.Email);
                    var roles = await _userManager.GetRolesAsync(user);
                    
                    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    {
                        return Redirect(returnUrl);
                    }
                    
                    return RedirectToAction("Dashboard", GetDashboardController(roles.FirstOrDefault()));
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                }
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private string GetDashboardController(string role)
        {
            return role switch
            {
                "Lecturer" => "Lecturer",
                "Coordinator" => "Coordinator",
                "Manager" => "Manager",
                "HR" => "HR",
                _ => "Home"
            };
        }

        private string GenerateEmployeeId(string role)
        {
            var prefix = role switch
            {
                "Lecturer" => "LEC",
                "Coordinator" => "COORD",
                "Manager" => "MGR",
                _ => "EMP"
            };
            
            return $"{prefix}-{DateTime.Now:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}";
        }

        private async Task CreateLecturerRecord(ApplicationUser user, RegisterViewModel model)
        {
            var lecturer = new Lecturer
            {
                UserId = user.Id,
                Name = user.FullName,
                Department = user.Department,
                HourlyRate = model.HourlyRate ?? 0
            };

            _context.Lecturers.Add(lecturer);
            await _context.SaveChangesAsync();
        }

        private async Task CreateCoordinatorRecord(ApplicationUser user)
        {
            var coordinator = new ProgrammeCoordinator
            {
                UserId = user.Id
            };

            _context.ProgrammeCoordinators.Add(coordinator);
            await _context.SaveChangesAsync();
        }

        private async Task CreateManagerRecord(ApplicationUser user)
        {
            var manager = new AcademicManager
            {
                UserId = user.Id
            };

            _context.AcademicManagers.Add(manager);
            await _context.SaveChangesAsync();
        }
    }
}