using PdfSharp.Fonts;

namespace Careers.Services;

/// <summary>
/// Serves Liberation Sans from the published app so PDFs render on Linux, where Arial is not installed.
/// </summary>
public sealed class EmbeddedFontResolver : IFontResolver
{
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var face = bold ? "LiberationSans-Bold" : "LiberationSans-Regular";
        return new FontResolverInfo(face);
    }

    public byte[] GetFont(string faceName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fonts", faceName + ".ttf");
        return File.ReadAllBytes(path);
    }
}
