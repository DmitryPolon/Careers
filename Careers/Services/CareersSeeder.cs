using Careers.Data;
using Careers.Models;
using Microsoft.AspNetCore.Identity;

namespace Careers.Services;

public sealed class CareersSeeder(
    ICareersStore store,
    IHostEnvironment environment,
    IConfiguration configuration,
    PasswordHasher<UserAccount> passwords)
{
    public const string DefaultAdminEmail = "admin@azpcloud.com";

    public void Seed()
    {
        if (!environment.IsDevelopment())
            return;

        SeedAdmin();
        SeedSamplePosition();
    }

    private void SeedAdmin()
    {
        var email = (configuration["SeedAdmin:Email"] ?? DefaultAdminEmail).Trim().ToLowerInvariant();
        var password = configuration["SeedAdmin:Password"];
        if (string.IsNullOrWhiteSpace(password) || store.FindUserByEmail(email) is not null)
            return;

        var admin = new UserAccount
        {
            Id = Guid.NewGuid().ToString("N"),
            Email = email,
            Role = "Admin",
            ReceivesApplications = true,
            CreatedAt = DateTime.UtcNow
        };
        admin.PasswordHash = passwords.HashPassword(admin, password);
        store.AddUser(admin);
    }

    private void SeedSamplePosition()
    {
        if (store.AnyPositions())
            return;

        store.Upsert(new Position
        {
            Id = "sample-platform-engineer",
            Title = "Platform engineer",
            Summary = "Build and look after the services behind AZPCloud.",
            Description =
                """
                AZPCloud is hiring a platform engineer to build and look after the services behind our products.

                You will work on the ASP.NET services, the deployment pipeline, and the operational details that keep customer sites available.

                This is a remote-friendly role. Screening questions are asked after you choose Apply.
                """,
            Status = PositionStatus.Open,
            UpdatedAt = DateTime.UtcNow,
            Questions =
            [
                new ScreeningQuestion
                {
                    Id = "work-auth",
                    Label = "Are you authorized to work in the United States?",
                    Type = QuestionType.YesNo,
                    Required = true
                },
                new ScreeningQuestion
                {
                    Id = "csharp-years",
                    Label = "How many years have you worked with C#?",
                    Type = QuestionType.Dropdown,
                    Required = true,
                    Options = ["Less than 2", "2 to 5", "More than 5"]
                },
                new ScreeningQuestion
                {
                    Id = "why",
                    Label = "Why do you want to work at AZPCloud?",
                    Type = QuestionType.LongText,
                    Required = true
                }
            ]
        });
    }
}
