using System.Security.Claims;
using Careers.Data;
using Careers.Models;
using Careers.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Careers.Endpoints;

public static class CareerEndpoints
{
    private static readonly HashSet<string> ResumeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx"
    };

    public static void Map(WebApplication app)
    {
        app.MapPost("/account/logout", Logout);
        app.MapPost("/admin/positions/{id}/status", SetStatus);
        app.MapPost("/admin/positions/upload", UploadPosition);
        app.MapGet("/admin/position-template.docx", DownloadTemplate);
        app.MapPost("/admin/admins/{id}/notifications", SetNotifications);
        app.MapPost("/jobs/{id}/apply/submit", Apply);
    }

    private static async Task<IResult> Logout(HttpContext http, IAntiforgery antiforgery)
    {
        await antiforgery.ValidateRequestAsync(http);
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/");
    }

    private static async Task<IResult> SetStatus(string id, HttpContext http, IAntiforgery antiforgery, ICareersStore store)
    {
        await antiforgery.ValidateRequestAsync(http);
        if (http.User.IsInRole("Admin") is false)
            return Results.Redirect("/account/login?returnUrl=/admin");

        if (!Enum.TryParse<PositionStatus>(http.Request.Form["status"], out var status))
            return Results.Redirect("/admin");

        store.SetStatus(id, status);
        return Results.Redirect("/admin");
    }

    private static async Task<IResult> SetNotifications(
        string id,
        HttpContext http,
        IAntiforgery antiforgery,
        ICareersStore store)
    {
        await antiforgery.ValidateRequestAsync(http);
        if (http.User.IsInRole("Admin") is false)
            return Results.Redirect("/account/login?returnUrl=/admin/admins");

        var receives = string.Equals(http.Request.Form["receives"], "true", StringComparison.OrdinalIgnoreCase);
        store.SetReceivesApplications(id, receives);
        return Results.Redirect("/admin/admins");
    }

    private static IResult DownloadTemplate(HttpContext http)
    {
        if (http.User.IsInRole("Admin") is false)
            return Results.Redirect("/account/login?returnUrl=/admin");

        return Results.File(
            DocxPositionTemplate.Create(),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "position-template.docx");
    }

    private static async Task<IResult> UploadPosition(HttpContext http, IAntiforgery antiforgery, ICareersStore store, CancellationToken cancellationToken)
    {
        await antiforgery.ValidateRequestAsync(http);
        if (http.User.IsInRole("Admin") is false)
            return Results.Redirect("/account/login?returnUrl=/admin");

        var file = http.Request.Form.Files.GetFile("jobFile");
        if (file is null || file.Length == 0)
            return UploadFailed(http, "Choose a Word .docx file.");

        if (file.Length > 5 * 1024 * 1024)
            return UploadFailed(http, "That file is larger than 5 MB.");

        var extension = Path.GetExtension(file.FileName);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return UploadFailed(http, "A PDF can be the write-up a person reads, but it does not say which lines are dropdowns or required. Upload a Word .docx file. The template has the questions table.");

        if (!extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return UploadFailed(http, "Upload a Word .docx file. The template shows where the title, description, and questions go.");

        await using var source = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var read = DocxPositionReader.Read(buffer);
        if (read.Position is null)
            return UploadFailed(http, string.Join(" ", read.Errors));

        read.Position.Id = Guid.NewGuid().ToString("N");
        store.Upsert(read.Position);
        return Results.Redirect($"/admin/edit/{read.Position.Id}?uploaded=1");
    }

    private static IResult UploadFailed(HttpContext http, string message)
    {
        if (message.Length > 800)
            message = message[..800];

        http.Response.Cookies.Append("careers-upload-error", message, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            MaxAge = TimeSpan.FromMinutes(5),
            SameSite = SameSiteMode.Lax,
            Secure = http.Request.IsHttps
        });
        return Results.Redirect("/admin");
    }

    private static async Task<IResult> Apply(
        string id,
        HttpContext http,
        IAntiforgery antiforgery,
        ICareersStore store,
        ApplicationPacketWriter packets,
        ApplicationMailer mailer,
        CancellationToken cancellationToken)
    {
        await antiforgery.ValidateRequestAsync(http);

        var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = http.User.FindFirst(ClaimTypes.Email)?.Value;
        if (userId is null || email is null)
            return Results.Redirect($"/account/login?returnUrl=/jobs/{Uri.EscapeDataString(id)}/apply");

        var position = store.FindPosition(id);
        if (position is null)
            return Results.Redirect("/not-found");

        if (position.Status != PositionStatus.Open)
            return Results.Redirect($"/jobs/{id}/apply?error=closed");

        if (store.FindApplication(userId, id) is not null)
            return Results.Redirect($"/jobs/{id}?applied=1");

        var form = http.Request.Form;
        var answers = new List<ApplicationAnswer>();
        foreach (var question in position.Questions)
        {
            var value = form[$"answers[{question.Id}]"].ToString().Trim();
            if (value.Length > 4000)
                value = value[..4000];

            if (!AnswerIsValid(question, value))
                return Results.Redirect($"/jobs/{id}/apply?error=required");

            answers.Add(new ApplicationAnswer
            {
                QuestionId = question.Id,
                Label = question.Label,
                Value = value
            });
        }

        var signature = form["signatureName"].ToString().Trim();
        var attested = string.Equals(form["attested"], "true", StringComparison.OrdinalIgnoreCase);
        if (signature.Length is < 2 or > 200 || !attested)
            return Results.Redirect($"/jobs/{id}/apply?error=required");

        var resume = form.Files.GetFile("resume");
        if (resume is null || resume.Length is 0 or > 5 * 1024 * 1024)
            return Results.Redirect($"/jobs/{id}/apply?error=resume");

        var extension = Path.GetExtension(resume.FileName);
        if (!ResumeExtensions.Contains(extension))
            return Results.Redirect($"/jobs/{id}/apply?error=resume");

        await using var body = resume.OpenReadStream();
        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, cancellationToken);

        var submittedAt = DateTime.UtcNow;
        var directory = await packets.WriteAsync(
            position,
            email,
            signature,
            submittedAt,
            answers,
            resume.FileName,
            buffer.ToArray(),
            cancellationToken);

        var saved = store.TryAddApplication(new JobApplication
        {
            Id = Path.GetFileName(directory),
            PositionId = position.Id,
            UserId = userId,
            Email = email,
            Answers = answers,
            SignatureName = signature,
            ResumeFileName = Path.GetFileName(resume.FileName),
            PacketDirectory = directory,
            SubmittedAt = submittedAt
        });

        if (!saved)
        {
            Directory.Delete(directory, recursive: true);
            return Results.Redirect($"/jobs/{id}?applied=1");
        }

        await mailer.SendApplicationAsync(
            store.ApplicationRecipientEmails(),
            position,
            email,
            signature,
            submittedAt,
            directory,
            cancellationToken);

        return Results.Redirect($"/jobs/{id}?applied=1");
    }

    private static bool AnswerIsValid(ScreeningQuestion question, string value)
    {
        if (question.Required && string.IsNullOrWhiteSpace(value))
            return false;

        return question.Type switch
        {
            QuestionType.YesNo => value is "Yes" or "No" || (!question.Required && string.IsNullOrEmpty(value)),
            QuestionType.Dropdown => string.IsNullOrEmpty(value) || question.Options.Contains(value, StringComparer.Ordinal),
            _ => true
        };
    }
}
