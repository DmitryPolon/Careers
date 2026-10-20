using Careers.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Careers.Services;

public static class ApplicationPdf
{
    public static byte[] Build(Position position, string email, string signatureName, DateTime submittedAt, IReadOnlyList<ApplicationAnswer> answers)
    {
        PdfSharp.PdfSharpLicense.SetAgplLicense();
        PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        using var document = new PdfDocument();
        document.Info.Title = position.Title;
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        var titleFont = new XFont("Arial", 16, XFontStyleEx.Bold);
        var labelFont = new XFont("Arial", 11, XFontStyleEx.Bold);
        var bodyFont = new XFont("Arial", 11);
        double y = 48;

        void NewPage()
        {
            gfx.Dispose();
            page = document.AddPage();
            gfx = XGraphics.FromPdfPage(page);
            y = 48;
        }

        void Write(string text, XFont font, double gapAfter)
        {
            var width = page.Width.Point - 96;
            foreach (var line in Wrap(gfx, text, font, width))
            {
                if (y > page.Height.Point - 64)
                    NewPage();

                gfx.DrawString(line, font, XBrushes.Black, new XRect(48, y, width, font.Size + 6), XStringFormats.TopLeft);
                y += font.Size + 5;
            }

            y += gapAfter;
        }

        Write(position.Title, titleFont, 10);
        Write($"Applicant: {email}", bodyFont, 2);
        Write($"Submitted (UTC): {submittedAt:yyyy-MM-dd HH:mm}", bodyFont, 2);
        Write($"Electronic signature: {signatureName}", bodyFont, 12);
        Write("Screening answers", labelFont, 8);

        foreach (var answer in answers)
        {
            Write(answer.Label, labelFont, 2);
            Write(string.IsNullOrWhiteSpace(answer.Value) ? "—" : answer.Value, bodyFont, 8);
        }

        Write("The applicant attested that the resume and these answers are true and complete.", bodyFont, 0);
        gfx.Dispose();

        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    private static IEnumerable<string> Wrap(XGraphics gfx, string text, XFont font, double width)
    {
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                yield return " ";
                continue;
            }

            var current = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && gfx.MeasureString(next, font).Width > width)
                {
                    yield return current;
                    current = word;
                }
                else
                {
                    current = next;
                }
            }

            if (current.Length > 0)
                yield return current;
        }
    }
}
