
using Microsoft.AspNetCore.Identity;
using ContractMonthlyClaimSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace ContractMonthlyClaimSystem.Data
{
    public static class DatabaseSeeder
    {
        public static async Task SeedAsync(ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            // Create roles if they don't exist
            string[] roles = { "Lecturer", "Coordinator", "Manager", "HR" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                    Console.WriteLine($"Created role: {role}");
                }
            }

            // Create default HR user if it doesn't exist
            var hrEmail = "hr@university.com";
            var hrUser = await userManager.FindByEmailAsync(hrEmail);

            if (hrUser == null)
            {
                hrUser = new ApplicationUser
                {
                    UserName = hrEmail,
                    Email = hrEmail,
                    FullName = "HR Manager",
                    Role = "HR",
                    Department = "Human Resources",
                    EmployeeId = "HR-001",
                    IsActive = true,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(hrUser, "HRpassword123!");

                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(hrUser, "HR");
                    Console.WriteLine("Created default HR user");
                }
                else
                {
                    Console.WriteLine($"Failed to create HR user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }

            // Create test users for other roles if they don't exist
            await CreateTestUser(userManager, "lecturer@university.com", "Test Lecturer", "Lecturer", "Computer Science", "LEC-001", "Lecturer123!");
            await CreateTestUser(userManager, "coordinator@university.com", "Test Coordinator", "Coordinator", "Administration", "COORD-001", "Coordinator123!");
            await CreateTestUser(userManager, "manager@university.com", "Test Manager", "Manager", "Management", "MGR-001", "Manager123!");

            await context.SaveChangesAsync();
        }

        private static async Task CreateTestUser(UserManager<ApplicationUser> userManager, string email, string fullName, string role, string department, string employeeId, string password)
        {
            var user = await userManager.FindByEmailAsync(email);

            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = fullName,
                    Role = role,
                    Department = department,
                    EmployeeId = employeeId,
                    IsActive = true,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(user, password);

                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, role);
                    Console.WriteLine($"Created {role} user: {email}");
                }
            }
        }
    }
}
