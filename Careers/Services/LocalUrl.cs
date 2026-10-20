namespace Careers.Services;

public static class LocalUrl
{
    public static string OrHome(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "/";

        if (!url.StartsWith('/') || url.StartsWith("//") || url.Contains('\\', StringComparison.Ordinal))
            return "/";

        return url;
    }
}
