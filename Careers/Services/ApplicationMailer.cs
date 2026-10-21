using Azure;
using Azure.Communication.Email;
using Careers.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Careers.Services;

public sealed class ApplicationMailer(IOptions<MailSettings> options, ILogger<ApplicationMailer> logger)
{
    public async Task SendApplicationAsync(
        IReadOnlyList<string> recipients,
        Position position,
        string applicantEmail,
        string signatureName,
        DateTime submittedAt,
        string packetDirectory)
    {
        var addresses = recipients
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var lines = new List<string>();
        if (addresses.Length == 0)
            lines.Add("No administrator is set to receive application PDFs.");
        else
            lines.AddRange(addresses);

        var settings = options.Value;
        if (addresses.Length == 0)
        {
            await File.WriteAllLinesAsync(Path.Combine(packetDirectory, "recipients.txt"), lines);
            return;
        }

        var subject = $"Application: {position.Title} — {applicantEmail}";
        var body =
            $"{applicantEmail} applied for {position.Title} on {submittedAt:u}.{Environment.NewLine}" +
            $"Signed: {signatureName}.{Environment.NewLine}{Environment.NewLine}" +
            "The application PDF and resume are attached.";
        var attachments = Directory.GetFiles(packetDirectory);

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            if (settings.UsesAzure)
                await SendAzureAsync(settings, addresses, subject, body, attachments, timeout.Token);
            else if (settings.IsReady)
                await SendSmtpAsync(settings, addresses, subject, body, attachments, timeout.Token);
            else if (OutlookMailbox.IsAvailable)
                OutlookMailbox.Send(addresses, subject, body, attachments);
            else
                throw new InvalidOperationException("No mailbox is configured. Set Mail:Host or install Outlook.");

            lines.Add("Sent.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not email the application for {PositionId}.", position.Id);
            lines.Add("Email was not sent. The PDF and resume were saved on the server.");
            lines.Add(exception.Message);
        }

        await File.WriteAllLinesAsync(Path.Combine(packetDirectory, "recipients.txt"), lines);
    }

    private static async Task SendAzureAsync(
        MailSettings settings,
        IReadOnlyList<string> addresses,
        string subject,
        string body,
        IReadOnlyList<string> attachments,
        CancellationToken cancellationToken)
    {
        var client = new EmailClient(settings.AzureConnectionString);
        var recipients = new EmailRecipients(addresses.Select(address => new EmailAddress(address)).ToList());
        var message = new EmailMessage(settings.From, recipients, new EmailContent(subject) { PlainText = body });
        foreach (var path in attachments)
        {
            var name = Path.GetFileName(path);
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            message.Attachments.Add(new EmailAttachment(name, ContentType(name), BinaryData.FromBytes(bytes)));
        }

        await client.SendAsync(WaitUntil.Completed, message, cancellationToken);
    }

    private static string ContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return "application/pdf";
        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        if (extension.Equals(".doc", StringComparison.OrdinalIgnoreCase))
            return "application/msword";
        return "application/octet-stream";
    }

    private static async Task SendSmtpAsync(
        MailSettings settings,
        IReadOnlyList<string> addresses,
        string subject,
        string body,
        IReadOnlyList<string> attachments,
        CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        foreach (var address in addresses)
            message.To.Add(MailboxAddress.Parse(address));

        message.Subject = subject;
        var builder = new BodyBuilder { TextBody = body };
        foreach (var file in attachments)
            builder.Attachments.Add(file);

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        var socket = settings.Port == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;
        await client.ConnectAsync(settings.Host, settings.Port, socket, cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.User))
            await client.AuthenticateAsync(settings.User, settings.Password, cancellationToken);

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
