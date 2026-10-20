using Careers.Models;

namespace Careers.Services;

public sealed class ApplicationPacketWriter(IWebHostEnvironment environment)
{
    public async Task<string> WriteAsync(
        Position position,
        string email,
        string signatureName,
        DateTime submittedAt,
        IReadOnlyList<ApplicationAnswer> answers,
        string resumeFileName,
        byte[] resumeBytes,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(environment.ContentRootPath, "App_Data", "outbox", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var pdf = ApplicationPdf.Build(position, email, signatureName, submittedAt, answers);
        await File.WriteAllBytesAsync(Path.Combine(directory, "application.pdf"), pdf, cancellationToken);

        var resumeName = CleanFileName(resumeFileName);
        await File.WriteAllBytesAsync(Path.Combine(directory, resumeName), resumeBytes, cancellationToken);
        return directory;
    }

    private static string CleanFileName(string name)
    {
        var file = Path.GetFileName(name);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(file.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "resume" : cleaned;
    }
}
