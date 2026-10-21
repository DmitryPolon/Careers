using Careers.Models;

namespace Careers.Data;

public interface ICareersStore
{
    IReadOnlyList<Position> OpenPositions();

    IReadOnlyList<Position> AllPositions();

    Position? FindPosition(string id);

    bool AnyPositions();

    void Upsert(Position position);

    bool SetStatus(string id, PositionStatus status);

    int CountApplications(string positionId);

    UserAccount? FindUserByEmail(string email);

    UserAccount? FindUser(string id);

    bool AddUser(UserAccount user);

    IReadOnlyList<UserAccount> Admins();

    bool SetReceivesApplications(string id, bool receives);

    bool TryDeleteAdmin(string id);

    IReadOnlyList<string> ApplicationRecipientEmails();

    JobApplication? FindApplication(string userId, string positionId);

    bool TryAddApplication(JobApplication application);
}
