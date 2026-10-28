using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;

namespace ShkafMFSB;

public class Database
{
    private const int PinHashIterations = 100_000;
    private const int PinSaltSize = 16;
    private const int PinHashSize = 32;
    private const int PinParts = 3;

    private readonly string _connStr;
    public string DbPath { get; }

    public Database(string? dbPath = null)
    {
        DbPath = dbPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shkaf_mfsb.db");
        _connStr = $"Data Source={DbPath}";
    }

    private SqliteConnection Open() { var c = new SqliteConnection(_connStr); c.Open(); return c; }

    public void Initialize(LockerConfig? lockerConfig = null)
    {
        var cfg = ValidateLockerConfig(lockerConfig);
        using var c = Open();
        c.Execute(@"
            CREATE TABLE IF NOT EXISTS Employees (Id INTEGER PRIMARY KEY AUTOINCREMENT, FullName TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Lockers (Id INTEGER PRIMARY KEY, EmployeeId INTEGER, IsService INTEGER, IsFaulty INTEGER, ModuleId INTEGER, ChannelId INTEGER);
            CREATE TABLE IF NOT EXISTS RfidTags (Uid TEXT PRIMARY KEY, EmployeeId INTEGER, Description TEXT);
            CREATE TABLE IF NOT EXISTS Logs (Id INTEGER PRIMARY KEY AUTOINCREMENT, Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP, EmployeeId INTEGER, LockerId INTEGER, Action TEXT, Details TEXT);
            CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);");

        // Установка PIN по умолчанию (хранится только хеш)
        var pinExists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM Settings WHERE Key = 'AdminPin'");
        if (pinExists == 0)
        {
            c.Execute("INSERT INTO Settings (Key, Value) VALUES ('AdminPin', @v)",
                new { v = HashPin("0000") });
        }
        else
        {
            // Миграция legacy-БД: если в Settings лежит открытый PIN — хешируем его
            var stored = c.ExecuteScalar<string>("SELECT Value FROM Settings WHERE Key = 'AdminPin'");
            if (!string.IsNullOrEmpty(stored) && !IsHashedPin(stored))
            {
                c.Execute("UPDATE Settings SET Value = @v WHERE Key = 'AdminPin'",
                    new { v = HashPin(stored) });
            }
        }

        var count = c.ExecuteScalar<int>("SELECT COUNT(*) FROM Lockers");
        if (count == 0)
        {
            for (int i = 1; i <= cfg.TotalCells; i++)
            {
                bool isService = i > cfg.ServiceCellThreshold;
                int mod = i <= cfg.Module1MaxCell ? 1 : 2;
                int ch = (i - 1) % cfg.Module1MaxCell;
                c.Execute("INSERT INTO Lockers (Id, IsService, IsFaulty, ModuleId, ChannelId) VALUES (@id, @s, 0, @m, @ch)",
                    new { id = i, s = isService ? 1 : 0, m = mod, ch = ch });
            }
        }
    }

    // === НАСТРОЙКИ (PIN) ===
    public bool VerifyPin(string pin)
    {
        if (pin is null) return false;
        using var c = Open();
        var stored = c.ExecuteScalar<string>("SELECT Value FROM Settings WHERE Key = 'AdminPin'");
        return VerifyPinHash(pin, stored);
    }

    public void UpdatePin(string newPin)
    {
        if (string.IsNullOrWhiteSpace(newPin))
            throw new ArgumentException("PIN не может быть пустым", nameof(newPin));

        using var c = Open();
        c.Execute("UPDATE Settings SET Value = @p WHERE Key = 'AdminPin'",
            new { p = HashPin(newPin) });
        AddLog(null, null, "ADMIN_UPDATE_PIN", "");
    }

    private static LockerConfig ValidateLockerConfig(LockerConfig? cfg)
    {
        cfg ??= new LockerConfig();
        if (cfg.TotalCells <= 0) cfg.TotalCells = 62;
        if (cfg.Module1MaxCell <= 0 || cfg.Module1MaxCell > cfg.TotalCells) cfg.Module1MaxCell = cfg.TotalCells / 2;
        if (cfg.ServiceCellThreshold < 0 || cfg.ServiceCellThreshold > cfg.TotalCells)
            cfg.ServiceCellThreshold = Math.Max(0, cfg.TotalCells - 2);
        return cfg;
    }

    private static string HashPin(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(PinSaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password: pin,
            salt: salt,
            iterations: PinHashIterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: PinHashSize);

        return $"{PinHashIterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPinHash(string pin, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

        var parts = stored.Split('.');
        if (parts.Length != PinParts || !int.TryParse(parts[0], out var iterations) || iterations <= 0)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password: pin,
                salt: salt,
                iterations: iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsHashedPin(string value)
    {
        var parts = value.Split('.');
        return parts.Length == PinParts && int.TryParse(parts[0], out _);
    }

    public string BackupDb()
    {
        string backupPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        File.Copy(DbPath, backupPath, true);
        return backupPath;
    }

    // === СОТРУДНИКИ ===
    public List<Employee> GetEmployees(string search = "")
    {
        using var c = Open();
        var sql = @"SELECT e.Id, e.FullName, (SELECT COUNT(*) FROM RfidTags WHERE EmployeeId = e.Id) as TagCount FROM Employees e";
        if (!string.IsNullOrEmpty(search)) sql += " WHERE e.FullName LIKE @s";
        sql += " ORDER BY e.FullName";

        var list = c.Query<Employee>(sql, new { s = $"%{search}%" }).ToList();
        foreach (var e in list)
        {
            var l = c.QueryFirstOrDefault<int?>("SELECT Id FROM Lockers WHERE EmployeeId = @id", new { id = e.Id });
            e.LockerDisplay = l.HasValue ? $"№ {l}" : "—";
        }
        return list;
    }

    public void AddEmployee(string name) { using var c = Open(); c.Execute("INSERT INTO Employees (FullName) VALUES (@n)", new { n = name }); AddLog(null, null, "ADMIN_ADD_EMPLOYEE", name); }
    public void UpdateEmployee(int id, string name) { using var c = Open(); c.Execute("UPDATE Employees SET FullName = @n WHERE Id = @id", new { n = name, id }); AddLog(id, null, "ADMIN_UPDATE_EMPLOYEE", name); }
    public void DeleteEmployee(int id) { using var c = Open(); c.Execute("UPDATE Lockers SET EmployeeId = NULL WHERE EmployeeId = @id", new { id }); c.Execute("DELETE FROM RfidTags WHERE EmployeeId = @id", new { id }); c.Execute("DELETE FROM Employees WHERE Id = @id", new { id }); AddLog(id, null, "ADMIN_DELETE_EMPLOYEE", $"id={id}"); }

    // === ЯЧЕЙКИ ===
    public List<Locker> GetLockers()
    {
        using var c = Open();
        return c.Query<Locker>(@"
            SELECT l.Id, l.EmployeeId, l.IsService, l.IsFaulty, l.ModuleId, l.ChannelId, e.FullName as OwnerName 
            FROM Lockers l LEFT JOIN Employees e ON e.Id = l.EmployeeId ORDER BY l.Id").ToList();
    }

    public void AssignLocker(int lockerId, int? employeeId) { using var c = Open(); c.Execute("UPDATE Lockers SET EmployeeId = @e WHERE Id = @id", new { e = employeeId, id = lockerId }); AddLog(employeeId, lockerId, "ADMIN_ASSIGN_LOCKER", $"locker={lockerId}, emp={(employeeId?.ToString(CultureInfo.InvariantCulture) ?? "null")}"); }
    public void UpdateHardware(int id, int module, int channel) { using var c = Open(); c.Execute("UPDATE Lockers SET ModuleId = @m, ChannelId = @ch WHERE Id = @id", new { m = module, ch = channel, id }); AddLog(null, id, "ADMIN_UPDATE_HARDWARE", $"module={module}, channel={channel}"); }
    public void SetFaulty(int id, bool isFaulty) { using var c = Open(); c.Execute("UPDATE Lockers SET IsFaulty = @f WHERE Id = @id", new { f = isFaulty ? 1 : 0, id }); AddLog(null, id, "ADMIN_SET_FAULTY", $"faulty={isFaulty}"); }
    public void SetServiceLockers(int id1, int id2) { using var c = Open(); c.Execute("UPDATE Lockers SET IsService = 0"); c.Execute("UPDATE Lockers SET IsService = 1, EmployeeId = NULL WHERE Id IN (@id1, @id2)", new { id1, id2 }); AddLog(null, null, "ADMIN_SET_SERVICE", $"id1={id1}, id2={id2}"); }

    // === МЕТКИ ===
    public List<RfidTag> GetTags() { using var c = Open(); return c.Query<RfidTag>(@"SELECT t.Uid, t.EmployeeId, t.Description, e.FullName as OwnerName FROM RfidTags t JOIN Employees e ON e.Id = t.EmployeeId").ToList(); }
    public void AddTag(string uid, int employeeId) { using var c = Open(); c.Execute("INSERT OR REPLACE INTO RfidTags (Uid, EmployeeId, Description) VALUES (@u, @e, 'Основная')", new { u = uid, e = employeeId }); AddLog(employeeId, null, "ADMIN_ADD_TAG", uid); }
    public void DeleteTag(string uid) { using var c = Open(); c.Execute("DELETE FROM RfidTags WHERE Uid = @u", new { u = uid }); AddLog(null, null, "ADMIN_DELETE_TAG", uid); }

    // === ЛОГИ ===
    public void AddLog(int? empId, int? lockerId, string action, string details = "") { using var c = Open(); c.Execute("INSERT INTO Logs (EmployeeId, LockerId, Action, Details) VALUES (@e, @l, @a, @d)", new { e = empId, l = lockerId, a = action, d = details }); }
    public List<LogEntry> GetLogs() { using var c = Open(); return c.Query<LogEntry>(@"SELECT a.Id, a.Timestamp, e.FullName as EmployeeName, a.Action, a.Details as TagUid, a.LockerId FROM Logs a LEFT JOIN Employees e ON e.Id = a.EmployeeId ORDER BY a.Id DESC LIMIT 100").ToList(); }
}
