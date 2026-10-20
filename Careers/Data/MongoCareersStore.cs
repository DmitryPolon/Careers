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
        try
        {
            client.GetDatabase("admin").RunCommand<BsonDocument>(new BsonDocument("ping", 1));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"MongoDB is not reachable at {connectionString}. Start the careers-mongo container.",
                exception);
        }

        var database = client.GetDatabase(databaseName);
        _positions = database.GetCollection<Position>("positions");
        _users = database.GetCollection<UserAccount>("users");
        _applications = database.GetCollection<JobApplication>("applications");
        EnsureIndexes();
    }

    public IReadOnlyList<Position> OpenPositions() =>
        _positions.Find(position => position.Status == PositionStatus.Open)
            .SortBy(position => position.Title)
            .ToList();

    public IReadOnlyList<Position> AllPositions() =>
        _positions.Find(FilterDefinition<Position>.Empty)
            .SortByDescending(position => position.UpdatedAt)
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
            .SortBy(user => user.Email)
            .ToList();

    public bool SetReceivesApplications(string id, bool receives)
    {
        var update = Builders<UserAccount>.Update.Set(user => user.ReceivesApplications, receives);
        var result = _users.UpdateOne(user => user.Id == id && user.Role == "Admin", update);
        return result.MatchedCount == 1;
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
