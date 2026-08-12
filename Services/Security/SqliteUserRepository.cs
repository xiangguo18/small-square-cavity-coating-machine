using Microsoft.Data.Sqlite;
using Small_square_cavity_coating_machine.Models.Security;
using System.Globalization;
using System.IO;

namespace Small_square_cavity_coating_machine.Services.Security;

public sealed class SqliteUserRepository : IUserRepository
{
    public const string BuiltInAdministratorName = "slkj";
    public const string BuiltInAdministratorInitialPassword = "slkj123456";

    private readonly string _connectionString;
    private readonly IPasswordHasher _passwordHasher;

    public SqliteUserRepository(string databasePath, IPasswordHasher passwordHasher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        ExecuteNonQuery(connection, transaction, """
            CREATE TABLE IF NOT EXISTS AppMetadata (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserName TEXT NOT NULL COLLATE NOCASE UNIQUE,
                PasswordHash TEXT NOT NULL,
                PasswordSalt TEXT NOT NULL,
                PasswordIterations INTEGER NOT NULL,
                IsBuiltInAdministrator INTEGER NOT NULL DEFAULT 0,
                AvatarData BLOB NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS UserPermissions (
                UserId INTEGER NOT NULL,
                PermissionKey INTEGER NOT NULL,
                IsAllowed INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (UserId, PermissionKey),
                FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
            );

            """);

        if (!ColumnExists(connection, transaction, "Users", "AvatarData"))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                "ALTER TABLE Users ADD COLUMN AvatarData BLOB NULL;");
        }

        ExecuteNonQuery(connection, transaction, """
            INSERT INTO AppMetadata(Key, Value)
            VALUES ('SchemaVersion', '2')
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """);

        EnsureBuiltInAdministrator(connection, transaction);
        transaction.Commit();
    }

    public IReadOnlyList<UserAccount> GetAll()
    {
        using var connection = OpenConnection();
        var permissions = ReadAllPermissions(connection);
        var users = new List<UserAccount>();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, UserName, IsBuiltInAdministrator, AvatarData
            FROM Users
            ORDER BY IsBuiltInAdministrator DESC, UserName COLLATE NOCASE;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            users.Add(new UserAccount(
                id,
                reader.GetString(1),
                reader.GetBoolean(2),
                permissions.GetValueOrDefault(id) ?? new HashSet<PermissionKey>(),
                reader.IsDBNull(3) ? null : (byte[])reader[3]));
        }

        return users;
    }

    public UserAccount? GetById(long id) => GetSingle("Id = $value", id);

    public UserAccount? GetByUserName(string userName) =>
        GetSingle("UserName = $value COLLATE NOCASE", NormalizeUserName(userName));

    public UserAccount Create(
        string userName,
        string password,
        IEnumerable<PermissionKey> permissions)
    {
        var normalizedName = NormalizeUserName(userName);
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("新建用户必须输入密码。", nameof(password));
        }

        var passwordHash = _passwordHasher.Hash(password);
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Users(
                    UserName, PasswordHash, PasswordSalt, PasswordIterations,
                    IsBuiltInAdministrator, CreatedUtc, UpdatedUtc)
                VALUES (
                    $name, $hash, $salt, $iterations,
                    0, $now, $now);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$name", normalizedName);
            command.Parameters.AddWithValue("$hash", passwordHash.Hash);
            command.Parameters.AddWithValue("$salt", passwordHash.Salt);
            command.Parameters.AddWithValue("$iterations", passwordHash.Iterations);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            var id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);

            WritePermissions(connection, transaction, id, permissions);
            transaction.Commit();
            return GetById(id)!;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("用户名已存在，请使用其他用户名。", exception);
        }
    }

    public UserAccount Update(
        long id,
        string userName,
        string? newPassword,
        IEnumerable<PermissionKey> permissions)
    {
        var existing = GetById(id) ?? throw new InvalidOperationException("要保存的用户已不存在。");
        var normalizedName = existing.UserName;
        IEnumerable<PermissionKey> effectivePermissions = existing.IsBuiltInAdministrator
            ? PermissionCatalog.All
            : NormalizePermissions(permissions);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;

            if (string.IsNullOrEmpty(newPassword))
            {
                command.CommandText = """
                    UPDATE Users
                    SET UserName = $name, UpdatedUtc = $now
                    WHERE Id = $id;
                    """;
            }
            else
            {
                var passwordHash = _passwordHasher.Hash(newPassword);
                command.CommandText = """
                    UPDATE Users
                    SET UserName = $name,
                        PasswordHash = $hash,
                        PasswordSalt = $salt,
                        PasswordIterations = $iterations,
                        UpdatedUtc = $now
                    WHERE Id = $id;
                    """;
                command.Parameters.AddWithValue("$hash", passwordHash.Hash);
                command.Parameters.AddWithValue("$salt", passwordHash.Salt);
                command.Parameters.AddWithValue("$iterations", passwordHash.Iterations);
            }

            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$name", normalizedName);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();

            WritePermissions(connection, transaction, id, effectivePermissions);
            transaction.Commit();
            return GetById(id)!;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("用户名已存在，请使用其他用户名。", exception);
        }
    }

    public void Delete(long id)
    {
        var existing = GetById(id);
        if (existing is null)
        {
            return;
        }
        if (existing.IsBuiltInAdministrator)
        {
            throw new InvalidOperationException("内置管理员账号不可删除。");
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Users WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public bool VerifyPassword(string userName, string password)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PasswordHash, PasswordSalt, PasswordIterations
            FROM Users
            WHERE UserName = $name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$name", NormalizeUserName(userName));
        using var reader = command.ExecuteReader();
        return reader.Read()
               && _passwordHasher.Verify(
                   password,
                   reader.GetString(0),
                   reader.GetString(1),
                   reader.GetInt32(2));
    }

    public UserAccount UpdateAvatar(long id, byte[]? avatarData)
    {
        if (avatarData is { Length: > 2_000_000 })
        {
            throw new ArgumentException("头像图片处理后仍然过大。", nameof(avatarData));
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Users
            SET AvatarData = $avatar, UpdatedUtc = $now
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$avatar", (object?)avatarData ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        if (command.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException("要修改头像的用户已不存在。");
        }

        return GetById(id)!;
    }

    public UserAccount ChangePassword(long id, string currentPassword, string newPassword)
    {
        var user = GetById(id) ?? throw new InvalidOperationException("当前用户已不存在。");
        if (!VerifyPassword(user.UserName, currentPassword))
        {
            throw new InvalidOperationException("原密码不正确。");
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            throw new ArgumentException("新密码不能为空。", nameof(newPassword));
        }

        return Update(user.Id, user.UserName, newPassword, user.Permissions);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private UserAccount? GetSingle(string predicate, object value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, UserName, IsBuiltInAdministrator, AvatarData
            FROM Users
            WHERE {predicate};
            """;
        command.Parameters.AddWithValue("$value", value);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var id = reader.GetInt64(0);
        var name = reader.GetString(1);
        var isAdministrator = reader.GetBoolean(2);
        var avatarData = reader.IsDBNull(3) ? null : (byte[])reader[3];
        reader.Close();
        return new UserAccount(
            id,
            name,
            isAdministrator,
            ReadPermissions(connection, id),
            avatarData);
    }

    private static Dictionary<long, IReadOnlySet<PermissionKey>> ReadAllPermissions(
        SqliteConnection connection)
    {
        var result = new Dictionary<long, IReadOnlySet<PermissionKey>>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT UserId, PermissionKey
            FROM UserPermissions
            WHERE IsAllowed = 1;
            """;
        using var reader = command.ExecuteReader();
        var mutable = new Dictionary<long, HashSet<PermissionKey>>();
        while (reader.Read())
        {
            var userId = reader.GetInt64(0);
            if (!mutable.TryGetValue(userId, out var set))
            {
                set = [];
                mutable.Add(userId, set);
            }

            if (Enum.IsDefined(typeof(PermissionKey), reader.GetInt32(1)))
            {
                set.Add((PermissionKey)reader.GetInt32(1));
            }
        }

        foreach (var pair in mutable)
        {
            result.Add(pair.Key, pair.Value);
        }

        return result;
    }

    private static IReadOnlySet<PermissionKey> ReadPermissions(
        SqliteConnection connection,
        long userId)
    {
        var result = new HashSet<PermissionKey>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT PermissionKey
            FROM UserPermissions
            WHERE UserId = $userId AND IsAllowed = 1;
            """;
        command.Parameters.AddWithValue("$userId", userId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var value = reader.GetInt32(0);
            if (Enum.IsDefined(typeof(PermissionKey), value))
            {
                result.Add((PermissionKey)value);
            }
        }

        return result;
    }

    private static void WritePermissions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long userId,
        IEnumerable<PermissionKey> permissions)
    {
        var allowed = NormalizePermissions(permissions);
        foreach (var permission in PermissionCatalog.All)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO UserPermissions(UserId, PermissionKey, IsAllowed)
                VALUES ($userId, $permission, $allowed)
                ON CONFLICT(UserId, PermissionKey)
                DO UPDATE SET IsAllowed = excluded.IsAllowed;
                """;
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$permission", (int)permission);
            command.Parameters.AddWithValue("$allowed", allowed.Contains(permission) ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private void EnsureBuiltInAdministrator(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        long? userId = null;
        using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = "SELECT Id FROM Users WHERE UserName = $name COLLATE NOCASE;";
            query.Parameters.AddWithValue("$name", BuiltInAdministratorName);
            var result = query.ExecuteScalar();
            if (result is not null && result is not DBNull)
            {
                userId = Convert.ToInt64(result, CultureInfo.InvariantCulture);
            }
        }

        if (userId is null)
        {
            var passwordHash = _passwordHasher.Hash(BuiltInAdministratorInitialPassword);
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO Users(
                    UserName, PasswordHash, PasswordSalt, PasswordIterations,
                    IsBuiltInAdministrator, CreatedUtc, UpdatedUtc)
                VALUES ($name, $hash, $salt, $iterations, 1, $now, $now);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$name", BuiltInAdministratorName);
            insert.Parameters.AddWithValue("$hash", passwordHash.Hash);
            insert.Parameters.AddWithValue("$salt", passwordHash.Salt);
            insert.Parameters.AddWithValue("$iterations", passwordHash.Iterations);
            insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            userId = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        else
        {
            using var protect = connection.CreateCommand();
            protect.Transaction = transaction;
            protect.CommandText = """
                UPDATE Users
                SET UserName = $name, IsBuiltInAdministrator = 1, UpdatedUtc = $now
                WHERE Id = $id;
                """;
            protect.Parameters.AddWithValue("$id", userId.Value);
            protect.Parameters.AddWithValue("$name", BuiltInAdministratorName);
            protect.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            protect.ExecuteNonQuery();
        }

        WritePermissions(connection, transaction, userId.Value, PermissionCatalog.All);
    }

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static bool ColumnExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<PermissionKey> NormalizePermissions(
        IEnumerable<PermissionKey> permissions) =>
        permissions.Where(PermissionCatalog.All.Contains).ToHashSet();

    private static string NormalizeUserName(string userName)
    {
        var normalized = userName?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("用户名不能为空。", nameof(userName));
        }

        return normalized;
    }
}
