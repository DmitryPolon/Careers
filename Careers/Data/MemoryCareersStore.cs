using Careers.Models;

namespace Careers.Data;

public sealed class MemoryCareersStore : ICareersStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Position> _positions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UserAccount> _usersById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UserAccount> _usersByEmail = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<JobApplication> _applications = [];

    public IReadOnlyList<Position> OpenPositions()
    {
        lock (_gate)
        {
            return _positions.Values
                .Where(position => position.Status == PositionStatus.Open)
                .OrderBy(position => position.Title, StringComparer.OrdinalIgnoreCase)
                .Select(Clone)
                .ToList();
        }
    }

    public IReadOnlyList<Position> AllPositions()
    {
        lock (_gate)
        {
            return _positions.Values
                .OrderByDescending(position => position.UpdatedAt)
                .Select(Clone)
                .ToList();
        }
    }

    public Position? FindPosition(string id)
    {
        lock (_gate)
            return _positions.TryGetValue(id, out var position) ? Clone(position) : null;
    }

    public void Upsert(Position position)
    {
        lock (_gate)
            _positions[position.Id] = Clone(position);
    }

    public bool SetStatus(string id, PositionStatus status)
    {
        lock (_gate)
        {
            if (!_positions.TryGetValue(id, out var position))
                return false;

            position.Status = status;
            position.UpdatedAt = DateTime.UtcNow;
            return true;
        }
    }

    public bool TryDeletePosition(string id)
    {
        lock (_gate)
        {
            if (!_positions.Remove(id))
                return false;

            _applications.RemoveAll(application => application.PositionId == id);
            return true;
        }
    }

    public int CountApplications(string positionId)
    {
        lock (_gate)
            return _applications.Count(application => application.PositionId == positionId);
    }

    public UserAccount? FindUserByEmail(string email)
    {
        lock (_gate)
            return _usersByEmail.TryGetValue(email, out var user) ? Clone(user) : null;
    }

    public UserAccount? FindUser(string id)
    {
        lock (_gate)
            return _usersById.TryGetValue(id, out var user) ? Clone(user) : null;
    }

    public bool AddUser(UserAccount user)
    {
        lock (_gate)
        {
            if (_usersByEmail.ContainsKey(user.Email))
                return false;

            var copy = Clone(user);
            _usersById[copy.Id] = copy;
            _usersByEmail[copy.Email] = copy;
            return true;
        }
    }

    public JobApplication? FindApplication(string userId, string positionId)
    {
        lock (_gate)
        {
            return _applications
                .Where(application => application.UserId == userId && application.PositionId == positionId)
                .Select(Clone)
                .FirstOrDefault();
        }
    }

    public bool TryAddApplication(JobApplication application)
    {
        lock (_gate)
        {
            if (_applications.Any(existing => existing.UserId == application.UserId && existing.PositionId == application.PositionId))
                return false;

            _applications.Add(Clone(application));
            return true;
        }
    }

    public IReadOnlyList<UserAccount> Admins()
    {
        lock (_gate)
        {
            return _usersById.Values
                .Where(user => user.Role == "Admin")
                .OrderBy(user => user.Email, StringComparer.OrdinalIgnoreCase)
                .Select(Clone)
                .ToList();
        }
    }

    public bool SetReceivesApplications(string id, bool receives)
    {
        lock (_gate)
        {
            if (!_usersById.TryGetValue(id, out var user) || user.Role != "Admin")
                return false;

            user.ReceivesApplications = receives;
            return true;
        }
    }

    public bool TryDeleteAdmin(string id)
    {
        lock (_gate)
        {
            if (!_usersById.TryGetValue(id, out var user) || user.Role != "Admin")
                return false;

            if (_usersById.Values.Count(account => account.Role == "Admin") <= 1)
                return false;

            _usersById.Remove(id);
            _usersByEmail.Remove(user.Email);
            return true;
        }
    }

    public IReadOnlyList<string> ApplicationRecipientEmails()
    {
        lock (_gate)
        {
            return _usersById.Values
                .Where(user => user.Role == "Admin" && user.ReceivesApplications)
                .Select(user => user.Email)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public bool AnyPositions()
    {
        lock (_gate)
            return _positions.Count > 0;
    }

    private static Position Clone(Position position) => new()
    {
        Id = position.Id,
        Title = position.Title,
        Summary = position.Summary,
        Description = position.Description,
        Status = position.Status,
        UpdatedAt = position.UpdatedAt,
        Questions = position.Questions.Select(question => new ScreeningQuestion
        {
            Id = question.Id,
            Label = question.Label,
            Type = question.Type,
            Required = question.Required,
            Options = [.. question.Options]
        }).ToList()
    };

    private static UserAccount Clone(UserAccount user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        PasswordHash = user.PasswordHash,
        Role = user.Role,
        ReceivesApplications = user.ReceivesApplications,
        CreatedAt = user.CreatedAt
    };

    private static JobApplication Clone(JobApplication application) => new()
    {
        Id = application.Id,
        PositionId = application.PositionId,
        UserId = application.UserId,
        Email = application.Email,
        SignatureName = application.SignatureName,
        ResumeFileName = application.ResumeFileName,
        PacketDirectory = application.PacketDirectory,
        SubmittedAt = application.SubmittedAt,
        Answers = application.Answers.Select(answer => new ApplicationAnswer
        {
            QuestionId = answer.QuestionId,
            Label = answer.Label,
            Value = answer.Value
        }).ToList()
    };
}
