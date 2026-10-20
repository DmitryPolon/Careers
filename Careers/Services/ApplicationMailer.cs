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
        string packetDirectory,
        CancellationToken cancellationToken)
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
            await File.WriteAllLinesAsync(Path.Combine(packetDirectory, "recipients.txt"), lines, cancellationToken);
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
            if (settings.IsReady)
                await SendSmtpAsync(settings, addresses, subject, body, attachments, cancellationToken);
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

        await File.WriteAllLinesAsync(Path.Combine(packetDirectory, "recipients.txt"), lines, cancellationToken);
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
