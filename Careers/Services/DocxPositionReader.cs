using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JobPosition = Careers.Models.Position;
using QuestionType = Careers.Models.QuestionType;
using ScreeningQuestion = Careers.Models.ScreeningQuestion;

namespace Careers.Services;

public sealed class DocxPositionReadResult
{
    public JobPosition? Position { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];

    public static DocxPositionReadResult Fail(params string[] errors) => new() { Errors = errors };
}

public static class DocxPositionReader
{
    public static DocxPositionReadResult Read(Stream stream)
    {
        try
        {
            using var word = WordprocessingDocument.Open(stream, false);
            var body = word.MainDocumentPart?.Document?.Body;
            if (body is null)
                return DocxPositionReadResult.Fail("That Word document has no content.");

            return ReadBody(body);
        }
        catch (OpenXmlPackageException)
        {
            return DocxPositionReadResult.Fail("That file is not a Word .docx document.");
        }
    }

    private static DocxPositionReadResult ReadBody(Body body)
    {
        var blocks = new List<string>();
        Table? questionTable = null;
        var inQuestions = false;

        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case Paragraph paragraph:
                    var text = Clean(paragraph.InnerText);
                    if (text.Length == 0)
                        continue;

                    if (IsQuestionsHeading(text))
                    {
                        inQuestions = true;
                        continue;
                    }

                    if (!inQuestions)
                        blocks.Add(text);
                    break;

                case Table table when questionTable is null && (inQuestions || LooksLikeQuestionTable(table)):
                    questionTable = table;
                    inQuestions = true;
                    break;

                case Table table when !inQuestions:
                    var flattened = string.Join("\n", table.Elements<TableRow>().Select(row => string.Join(" | ", Cells(row))));
                    if (flattened.Length > 0)
                        blocks.Add(flattened);
                    break;
            }
        }

        var errors = new List<string>();
        if (blocks.Count == 0)
            errors.Add("The document needs a title on the first line.");

        var title = blocks.Count == 0 ? "" : StripLabel(blocks[0], "Title:", "Job title:", "Position:");
        if (blocks.Count > 0)
            blocks.RemoveAt(0);

        if (string.IsNullOrWhiteSpace(title))
            errors.Add("The first line of the document is the job title.");
        else if (title.Length > 200)
            errors.Add("The job title must be 200 characters or fewer.");

        string? summary = null;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (!blocks[i].StartsWith("Summary:", StringComparison.OrdinalIgnoreCase))
                continue;

            summary = blocks[i]["Summary:".Length..].Trim();
            blocks.RemoveAt(i);
            break;
        }

        var description = string.Join("\n\n", blocks).Trim();
        if (string.IsNullOrWhiteSpace(description))
            errors.Add("Add the job description after the title.");

        if (string.IsNullOrWhiteSpace(summary))
        {
            var firstLine = description.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            summary = firstLine.Length <= 180 ? firstLine : firstLine[..180].TrimEnd() + "…";
        }
        else if (summary.Length > 300)
        {
            errors.Add("The summary must be 300 characters or fewer.");
        }

        var questions = ReadQuestions(questionTable, errors);
        if (errors.Count > 0)
            return new DocxPositionReadResult { Errors = errors };

        return new DocxPositionReadResult
        {
            Position = new JobPosition
            {
                Title = title.Trim(),
                Summary = summary.Trim(),
                Description = description,
                Status = Careers.Models.PositionStatus.Hold,
                Questions = questions,
                UpdatedAt = DateTime.UtcNow
            }
        };
    }

    private static List<ScreeningQuestion> ReadQuestions(Table? table, List<string> errors)
    {
        if (table is null)
        {
            errors.Add("Add a heading named Screening questions, then a table with the columns Question, Type, Required, and Choices.");
            return [];
        }

        var rows = table.Elements<TableRow>().Select(Cells).Where(cells => cells.Any(cell => cell.Length > 0)).ToList();
        if (rows.Count == 0)
        {
            errors.Add("The screening questions table is empty.");
            return [];
        }

        var start = rows[0][0].Contains("question", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (start >= rows.Count)
        {
            errors.Add("Add at least one row under the Question, Type, Required, and Choices headings.");
            return [];
        }

        var questions = new List<ScreeningQuestion>();
        for (var i = start; i < rows.Count && questions.Count < 30; i++)
        {
            var cells = rows[i];
            var label = cells.ElementAtOrDefault(0) ?? "";
            var typeText = cells.ElementAtOrDefault(1) ?? "";
            var requiredText = cells.ElementAtOrDefault(2) ?? "";
            var choicesText = cells.ElementAtOrDefault(3) ?? "";
            var rowNumber = i + 1;

            if (string.IsNullOrWhiteSpace(label))
            {
                errors.Add($"Row {rowNumber} needs a question.");
                continue;
            }

            if (!TryParseType(typeText, out var type))
            {
                errors.Add($"Row {rowNumber} has an unknown type \"{typeText}\". Use Short text, Long text, Dropdown, or Yes/No.");
                continue;
            }

            if (!TryParseRequired(requiredText, out var required))
            {
                errors.Add($"Row {rowNumber} must say Yes or No in the Required column.");
                continue;
            }

            var options = SplitChoices(choicesText);
            if (type == QuestionType.Dropdown && options.Count == 0)
                errors.Add($"Row {rowNumber} is a dropdown and needs choices separated by semicolons.");

            questions.Add(new ScreeningQuestion
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = label.Trim(),
                Type = type,
                Required = required,
                Options = type == QuestionType.Dropdown ? options : []
            });
        }

        if (rows.Count - start > 30)
            errors.Add("A position can have at most 30 screening questions.");

        return questions;
    }

    private static bool LooksLikeQuestionTable(Table table)
    {
        var first = table.Elements<TableRow>().FirstOrDefault();
        if (first is null)
            return false;

        var cells = Cells(first);
        return cells.Count >= 2 && cells[0].Contains("question", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> Cells(TableRow row) =>
        row.Elements<TableCell>().Select(cell => Clean(cell.InnerText)).ToList();

    private static List<string> SplitChoices(string text) =>
        text.Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToList();

    private static bool TryParseType(string text, out QuestionType type)
    {
        var value = new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        switch (value)
        {
            case "shorttext":
            case "text":
            case "short":
                type = QuestionType.ShortText;
                return true;
            case "longtext":
            case "textarea":
            case "long":
            case "paragraph":
                type = QuestionType.LongText;
                return true;
            case "dropdown":
            case "select":
            case "list":
                type = QuestionType.Dropdown;
                return true;
            case "yesno":
            case "boolean":
                type = QuestionType.YesNo;
                return true;
            default:
                type = default;
                return false;
        }
    }

    private static bool TryParseRequired(string text, out bool required)
    {
        var value = text.Trim().ToLowerInvariant();
        switch (value)
        {
            case "":
            case "yes":
            case "y":
            case "true":
            case "required":
                required = true;
                return true;
            case "no":
            case "n":
            case "false":
            case "optional":
                required = false;
                return true;
            default:
                required = false;
                return false;
        }
    }

    private static bool IsQuestionsHeading(string text)
    {
        var value = text.Trim().TrimEnd(':').ToLowerInvariant();
        return value is "screening questions" or "qualifying questions" or "qualification questions";
    }

    private static string StripLabel(string text, params string[] labels)
    {
        foreach (var label in labels)
        {
            if (text.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                return text[label.Length..].Trim();
        }

        return text.Trim();
    }

    private static string Clean(string? text) =>
        string.Join(" ", (text ?? "").Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
}
