namespace Careers.Services;

public sealed class MailSettings
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public string User { get; set; } = "";

    public string Password { get; set; } = "";

    public string From { get; set; } = "careers@azpcloud.com";

    public bool IsReady => !string.IsNullOrWhiteSpace(Host);
}
