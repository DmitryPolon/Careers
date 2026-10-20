using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Careers.Services;

public static class DocxPositionTemplate
{
    public static byte[] Create()
    {
        using var stream = new MemoryStream();
        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = word.AddMainDocumentPart();
            main.Document = new Document(new Body(
                Text("Customer support specialist"),
                Text("Summary: Help AZPCloud customers get started and resolve account questions."),
                Text("You will answer product questions, write clear replies, and hand engineering issues to the platform team."),
                Text("This role is remote-friendly. Applicants see this write-up on the job page. The table below becomes the form they fill in after they apply."),
                Text("Screening questions"),
                QuestionTable(),
                new SectionProperties()));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static Paragraph Text(string value) =>
        new(new Run(new Text(value) { Space = SpaceProcessingModeValues.Preserve }));

    private static Table QuestionTable()
    {
        var table = new Table();
        table.Append(new TableRow(
            Cell("Question"),
            Cell("Type"),
            Cell("Required"),
            Cell("Choices")));
        table.Append(new TableRow(
            Cell("Are you authorized to work in the United States?"),
            Cell("Yes/No"),
            Cell("Yes"),
            Cell("")));
        table.Append(new TableRow(
            Cell("How many years have you worked with customers?"),
            Cell("Dropdown"),
            Cell("Yes"),
            Cell("Less than 2; 2 to 5; More than 5")));
        table.Append(new TableRow(
            Cell("Why do you want to work at AZPCloud?"),
            Cell("Long text"),
            Cell("Yes"),
            Cell("")));
        return table;
    }

    private static TableCell Cell(string value) =>
        new(new Paragraph(new Run(new Text(value) { Space = SpaceProcessingModeValues.Preserve })));
}
