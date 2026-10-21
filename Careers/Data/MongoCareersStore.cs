using Careers.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace Careers.Data;

public sealed class MongoCareersStore : ICareersStore
{
    private readonly IMongoCollection<Position> _positions;
    private readonly IMongoCollection<UserAccount> _users;
    private readonly IMongoCollection<JobApplication> _applications;

    static MongoCareersStore()
    {
        var pack = new ConventionPack
        {
            new EnumRepresentationConvention(BsonType.String)
        };
        ConventionRegistry.Register("careers-enums", pack, type => type.Namespace == "Careers.Models");
    }

    public MongoCareersStore(IConfiguration configuration)
    {
        var connectionString = configuration["Mongo:ConnectionString"] ?? "mongodb://localhost:27017";
        var databaseName = configuration["Mongo:Database"] ?? "careers";
        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);
        try
        {
            database.RunCommand<BsonDocument>(new BsonDocument("ping", 1));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("MongoDB is not reachable.", exception);
        }
        _positions = database.GetCollection<Position>("positions");
        _users = database.GetCollection<UserAccount>("users");
        _applications = database.GetCollection<JobApplication>("applications");
        EnsureIndexes();
    }

    public IReadOnlyList<Position> OpenPositions() =>
        _positions.Find(position => position.Status == PositionStatus.Open)
            .ToList()
            .OrderBy(position => position.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<Position> AllPositions() =>
        _positions.Find(FilterDefinition<Position>.Empty)
            .ToList()
            .OrderByDescending(position => position.UpdatedAt)
            .ToList();

    public Position? FindPosition(string id) =>
        _positions.Find(position => position.Id == id).FirstOrDefault();

    public bool AnyPositions() =>
        _positions.Find(FilterDefinition<Position>.Empty).Limit(1).Any();

    public void Upsert(Position position)
    {
        position.UpdatedAt = DateTime.UtcNow;
        _positions.ReplaceOne(
            existing => existing.Id == position.Id,
            position,
            new ReplaceOptions { IsUpsert = true });
    }

    public bool SetStatus(string id, PositionStatus status)
    {
        var update = Builders<Position>.Update
            .Set(position => position.Status, status)
            .Set(position => position.UpdatedAt, DateTime.UtcNow);
        var result = _positions.UpdateOne(position => position.Id == id, update);
        return result.MatchedCount == 1;
    }

    public bool TryDeletePosition(string id)
    {
        var result = _positions.DeleteOne(position => position.Id == id);
        if (result.DeletedCount != 1)
            return false;

        _applications.DeleteMany(application => application.PositionId == id);
        return true;
    }

    public int CountApplications(string positionId) =>
        (int)_applications.CountDocuments(application => application.PositionId == positionId);

    public UserAccount? FindUserByEmail(string email) =>
        _users.Find(user => user.Email == email).FirstOrDefault();

    public UserAccount? FindUser(string id) =>
        _users.Find(user => user.Id == id).FirstOrDefault();

    public bool AddUser(UserAccount user)
    {
        try
        {
            _users.InsertOne(user);
            return true;
        }
        catch (MongoWriteException exception) when (IsDuplicate(exception))
        {
            return false;
        }
    }

    public JobApplication? FindApplication(string userId, string positionId) =>
        _applications.Find(application => application.UserId == userId && application.PositionId == positionId)
            .FirstOrDefault();

    public bool TryAddApplication(JobApplication application)
    {
        try
        {
            _applications.InsertOne(application);
            return true;
        }
        catch (MongoWriteException exception) when (IsDuplicate(exception))
        {
            return false;
        }
    }

    private void EnsureIndexes()
    {
        _users.Indexes.CreateOne(new CreateIndexModel<UserAccount>(
            Builders<UserAccount>.IndexKeys.Ascending(user => user.Email),
            new CreateIndexOptions { Unique = true, Name = "users_email" }));

        _applications.Indexes.CreateOne(new CreateIndexModel<JobApplication>(
            Builders<JobApplication>.IndexKeys
                .Ascending(application => application.PositionId)
                .Ascending(application => application.UserId),
            new CreateIndexOptions { Unique = true, Name = "applications_position_user" }));

        var users = _users.Database.GetCollection<BsonDocument>("users");
        var missingFlag = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("Role", "Admin"),
            Builders<BsonDocument>.Filter.Exists("ReceivesApplications", false));
        users.UpdateMany(missingFlag, Builders<BsonDocument>.Update.Set("ReceivesApplications", true));
    }

    public IReadOnlyList<UserAccount> Admins() =>
        _users.Find(user => user.Role == "Admin")
            .ToList()
            .OrderBy(user => user.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public bool SetReceivesApplications(string id, bool receives)
    {
        var update = Builders<UserAccount>.Update.Set(user => user.ReceivesApplications, receives);
        var result = _users.UpdateOne(user => user.Id == id && user.Role == "Admin", update);
        return result.MatchedCount == 1;
    }

    public bool TryDeleteAdmin(string id)
    {
        var user = FindUser(id);
        if (user is null || user.Role != "Admin" || Admins().Count <= 1)
            return false;

        var result = _users.DeleteOne(existing => existing.Id == id && existing.Role == "Admin");
        return result.DeletedCount == 1;
    }

    public IReadOnlyList<string> ApplicationRecipientEmails() =>
        _users.Find(user => user.Role == "Admin" && user.ReceivesApplications)
            .ToList()
            .Select(user => user.Email)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsDuplicate(MongoWriteException exception) =>
        exception.WriteError?.Category == ServerErrorCategory.DuplicateKey;
}
