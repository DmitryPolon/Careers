namespace Careers.Models;

public enum PositionStatus
{
    Open,
    Hold,
    Closed
}

public enum QuestionType
{
    ShortText,
    LongText,
    Dropdown,
    YesNo
}

public sealed class ScreeningQuestion
{
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";

    public QuestionType Type { get; set; }

    public bool Required { get; set; }

    public List<string> Options { get; set; } = [];
}

public sealed class Position
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string Summary { get; set; } = "";

    public string Description { get; set; } = "";

    public PositionStatus Status { get; set; }

    public List<ScreeningQuestion> Questions { get; set; } = [];

    public DateTime UpdatedAt { get; set; }
}

public sealed class UserAccount
{
    public string Id { get; set; } = "";

    public string Email { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public string Role { get; set; } = "Applicant";

    public bool ReceivesApplications { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class ApplicationAnswer
{
    public string QuestionId { get; set; } = "";

    public string Label { get; set; } = "";

    public string Value { get; set; } = "";
}

public sealed class JobApplication
{
    public string Id { get; set; } = "";

    public string PositionId { get; set; } = "";

    public string UserId { get; set; } = "";

    public string Email { get; set; } = "";

    public List<ApplicationAnswer> Answers { get; set; } = [];

    public string SignatureName { get; set; } = "";

    public string ResumeFileName { get; set; } = "";

    public string PacketDirectory { get; set; } = "";

    public DateTime SubmittedAt { get; set; }
}

public sealed class QuestionDraft
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Label { get; set; } = "";

    public QuestionType Type { get; set; } = QuestionType.ShortText;

    public bool Required { get; set; } = true;

    public string Options { get; set; } = "";
}

public sealed class PositionDraft
{
    public string Title { get; set; } = "";

    public string Summary { get; set; } = "";

    public string Description { get; set; } = "";

    public PositionStatus Status { get; set; } = PositionStatus.Open;

    public List<QuestionDraft> Questions { get; set; } = [];

    public static PositionDraft From(Position position) => new()
    {
        Title = position.Title,
        Summary = position.Summary,
        Description = position.Description,
        Status = position.Status,
        Questions = position.Questions.Select(question => new QuestionDraft
        {
            Id = question.Id,
            Label = question.Label,
            Type = question.Type,
            Required = question.Required,
            Options = string.Join(Environment.NewLine, question.Options)
        }).ToList()
    };

    public Position ToPosition(string id) => new()
    {
        Id = id,
        Title = Title.Trim(),
        Summary = Summary.Trim(),
        Description = Description.Trim(),
        Status = Status,
        Questions = Questions.Select(question => new ScreeningQuestion
        {
            Id = string.IsNullOrWhiteSpace(question.Id) ? Guid.NewGuid().ToString("N") : question.Id,
            Label = question.Label.Trim(),
            Type = question.Type,
            Required = question.Required,
            Options = question.Type == QuestionType.Dropdown
                ? question.Options
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.Ordinal)
                    .Take(20)
                    .ToList()
                : []
        }).ToList()
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Title))
            errors.Add("A title is required.");
        if (string.IsNullOrWhiteSpace(Summary))
            errors.Add("A short summary is required.");
        if (string.IsNullOrWhiteSpace(Description))
            errors.Add("A description is required.");

        foreach (var question in Questions)
        {
            if (string.IsNullOrWhiteSpace(question.Label))
            {
                errors.Add("Every screening question needs a label.");
                break;
            }
        }

        if (Questions.Any(question => question.Type == QuestionType.Dropdown &&
            string.IsNullOrWhiteSpace(question.Options)))
        {
            errors.Add("A dropdown question needs at least one choice.");
        }

        return errors;
    }
}
