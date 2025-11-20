using System.Diagnostics;
using ContractMonthlyClaimSystem.Models;
using Microsoft.AspNetCore.Mvc;

namespace ContractMonthlyClaimSystem.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            if (User.Identity.IsAuthenticated)
            {
                if (User.IsInRole("Lecturer"))
                    return RedirectToAction("Dashboard", "Lecturer");
                else if (User.IsInRole("Coordinator"))
                    return RedirectToAction("Dashboard", "Coordinator");
                else if (User.IsInRole("Manager"))
                    return RedirectToAction("Dashboard", "Manager");
                else if (User.IsInRole("HR")) // Add this line for HR
                    return RedirectToAction("Dashboard", "HR");
            }
            return View();
        }

        // Add this development login method
        [HttpGet]
        [Route("dev-login")]
        public IActionResult DevLogin()
        {
            return View();
        }

        [HttpPost]
        [Route("dev-login")]
        public IActionResult DevLogin(string role)
        {
            // Simple role-based redirect for development
            return role?.ToLower() switch
            {
                "manager" => RedirectToAction("Dashboard", "Manager"),
                "coordinator" => RedirectToAction("Dashboard", "Coordinator"),
                "lecturer" => RedirectToAction("Dashboard", "Lecturer"),
                "hr" => RedirectToAction("Dashboard", "HR"), // Add this line
                _ => RedirectToAction("Index", "Home")
            };
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}