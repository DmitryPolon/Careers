namespace Careers.Services;

public static class OutlookMailbox
{
    public static bool IsAvailable =>
        OperatingSystem.IsWindows() && Type.GetTypeFromProgID("Outlook.Application") is not null;

    public static void Send(
        IReadOnlyList<string> recipients,
        string subject,
        string body,
        IReadOnlyList<string> attachmentPaths)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Outlook is only available on Windows.");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SendOnSta(recipients, subject, body, attachmentPaths);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw failure;
    }

    private static void SendOnSta(
        IReadOnlyList<string> recipients,
        string subject,
        string body,
        IReadOnlyList<string> attachmentPaths)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Outlook is only available on Windows.");

        var outlookType = Type.GetTypeFromProgID("Outlook.Application")
            ?? throw new InvalidOperationException("Outlook is not installed on this computer.");
        dynamic outlook = Activator.CreateInstance(outlookType)
            ?? throw new InvalidOperationException("Outlook did not start.");
        dynamic session = outlook.GetNamespace("MAPI");
        session.Logon("Outlook", Type.Missing, false, true);

        dynamic message = outlook.CreateItem(0);
        message.To = string.Join("; ", recipients);
        message.Subject = subject;
        message.Body = body;

        dynamic accounts = session.Accounts;
        int count = accounts.Count;
        for (var index = 1; index <= count; index++)
        {
            dynamic account = accounts.Item(index);
            string smtp = account.SmtpAddress;
            if (recipients.Contains(smtp, StringComparer.OrdinalIgnoreCase))
            {
                message.SendUsingAccount = account;
                break;
            }
        }

        foreach (var path in attachmentPaths)
            message.Attachments.Add(path);

        message.Send();
    }
}
