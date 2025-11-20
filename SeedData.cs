using Microsoft.AspNetCore.Identity;
using ContractMonthlyClaimSystem.Models;
using ContractMonthlyClaimSystem.Data;

namespace ContractMonthlyClaimSystem
{
    public static class SeedData
    {
        public static async Task Initialize(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

            string[] roleNames = { "Lecturer", "Coordinator", "Manager", "HR" };

            foreach (var roleName in roleNames)
            {
                var roleExist = await roleManager.RoleExistsAsync(roleName);
                if (!roleExist)
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            // Create default admin user
            var adminUser = await userManager.FindByEmailAsync("admin@university.com");
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = "admin@university.com",
                    Email = "admin@university.com",
                    FullName = "System Administrator",
                    Role = "Manager",
                    Department = "Administration",
                    EmployeeId = "MGR-20240101-ADMIN",
                    EmailConfirmed = true
                };

                var createPowerUser = await userManager.CreateAsync(adminUser, "Admin123!");
                if (createPowerUser.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Manager");

                    // Create manager record
                    var manager = new AcademicManager
                    {
                        UserId = adminUser.Id
                    };
                    context.AcademicManagers.Add(manager);
                    await context.SaveChangesAsync();
                }
            }

            // Create sample lecturer
            var lecturerUser = await userManager.FindByEmailAsync("lecturer@university.com");
            if (lecturerUser == null)
            {
                lecturerUser = new ApplicationUser
                {
                    UserName = "lecturer@university.com",
                    Email = "lecturer@university.com",
                    FullName = "Dr. John Smith",
                    Role = "Lecturer",
                    Department = "Computer Science",
                    HourlyRate = 50.00m,
                    EmployeeId = "LEC-20240101-0001",
                    EmailConfirmed = true
                };

                var createLecturer = await userManager.CreateAsync(lecturerUser, "Lecturer123!");
                if (createLecturer.Succeeded)
                {
                    await userManager.AddToRoleAsync(lecturerUser, "Lecturer");

                    var lecturer = new Lecturer
                    {
                        UserId = lecturerUser.Id,
                        Name = lecturerUser.FullName,
                        Department = lecturerUser.Department,
                        HourlyRate = lecturerUser.HourlyRate.Value
                    };
                    context.Lecturers.Add(lecturer);
                    await context.SaveChangesAsync();
                }
            }

            // Create sample coordinator
            var coordinatorUser = await userManager.FindByEmailAsync("coordinator@university.com");
            if (coordinatorUser == null)
            {
                coordinatorUser = new ApplicationUser
                {
                    UserName = "coordinator@university.com",
                    Email = "coordinator@university.com",
                    FullName = "Prof. Sarah Johnson",
                    Role = "Coordinator",
                    Department = "Engineering",
                    EmployeeId = "COORD-20240101-0001",
                    EmailConfirmed = true
                };

                var createCoordinator = await userManager.CreateAsync(coordinatorUser, "Coordinator123!");
                if (createCoordinator.Succeeded)
                {
                    await userManager.AddToRoleAsync(coordinatorUser, "Coordinator");

                    var coordinator = new ProgrammeCoordinator
                    {
                        UserId = coordinatorUser.Id
                    };
                    context.ProgrammeCoordinators.Add(coordinator);
                    await context.SaveChangesAsync();
                }
            }
        }
    }
}