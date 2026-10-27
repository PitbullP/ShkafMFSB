using Microsoft.Data.Sqlite;
using Dapper;
using System.IO;

namespace ShkafMFSB;

public class Database
{
    private readonly string _connStr;
    public string DbPath { get; }

    public Database()
    {
        DbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shkaf_mfsb.db");
        _connStr = $"Data Source={DbPath}";
    }

    private SqliteConnection Open() { var c = new SqliteConnection(_connStr); c.Open(); return c; }

    public void Initialize()
    {
        using var c = Open();
        c.Execute(@"
            CREATE TABLE IF NOT EXISTS Employees (Id INTEGER PRIMARY KEY AUTOINCREMENT, FullName TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Lockers (Id INTEGER PRIMARY KEY, EmployeeId INTEGER, IsService INTEGER, IsFaulty INTEGER, ModuleId INTEGER, ChannelId INTEGER);
            CREATE TABLE IF NOT EXISTS RfidTags (Uid TEXT PRIMARY KEY, EmployeeId INTEGER, Description TEXT);
            CREATE TABLE IF NOT EXISTS Logs (Id INTEGER PRIMARY KEY AUTOINCREMENT, Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP, EmployeeId INTEGER, LockerId INTEGER, Action TEXT, Details TEXT);
            CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);");

        // Установка PIN по умолчанию
        var pinExists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM Settings WHERE Key = 'AdminPin'");
        if (pinExists == 0) c.Execute("INSERT INTO Settings (Key, Value) VALUES ('AdminPin', '0000')");

        var count = c.ExecuteScalar<int>("SELECT COUNT(*) FROM Lockers");
        if (count == 0)
        {
            for (int i = 1; i <= 62; i++)
            {
                bool isService = i > 60;
                int mod = i <= 32 ? 1 : 2;
                int ch = (i - 1) % 32;
                c.Execute("INSERT INTO Lockers (Id, IsService, IsFaulty, ModuleId, ChannelId) VALUES (@id, @s, 0, @m, @ch)", 
                    new { id = i, s = isService ? 1 : 0, m = mod, ch = ch });
            }
        }
    }

    // === НАСТРОЙКИ ===
    public bool VerifyPin(string pin)
    {
        using var c = Open();
        return c.ExecuteScalar<string>("SELECT Value FROM Settings WHERE Key = 'AdminPin'") == pin;
    }

    public void UpdatePin(string newPin)
    {
        using var c = Open();
        c.Execute("UPDATE Settings SET Value = @p WHERE Key = 'AdminPin'", new { p = newPin });
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

    public void AddEmployee(string name) { using var c = Open(); c.Execute("INSERT INTO Employees (FullName) VALUES (@n)", new { n = name }); }
    public void UpdateEmployee(int id, string name) { using var c = Open(); c.Execute("UPDATE Employees SET FullName = @n WHERE Id = @id", new { n = name, id }); }
    public void DeleteEmployee(int id) { using var c = Open(); c.Execute("UPDATE Lockers SET EmployeeId = NULL WHERE EmployeeId = @id", new { id }); c.Execute("DELETE FROM RfidTags WHERE EmployeeId = @id", new { id }); c.Execute("DELETE FROM Employees WHERE Id = @id", new { id }); }

    // === ЯЧЕЙКИ ===
    public List<Locker> GetLockers()
    {
        using var c = Open();
        return c.Query<Locker>(@"
            SELECT l.Id, l.EmployeeId, l.IsService, l.IsFaulty, l.ModuleId, l.ChannelId, e.FullName as OwnerName 
            FROM Lockers l LEFT JOIN Employees e ON e.Id = l.EmployeeId ORDER BY l.Id").ToList();
    }

    public void AssignLocker(int lockerId, int? employeeId) { using var c = Open(); c.Execute("UPDATE Lockers SET EmployeeId = @e WHERE Id = @id", new { e = employeeId, id = lockerId }); }
    public void UpdateHardware(int id, int module, int channel) { using var c = Open(); c.Execute("UPDATE Lockers SET ModuleId = @m, ChannelId = @ch WHERE Id = @id", new { m = module, ch = channel, id }); }
    public void SetFaulty(int id, bool isFaulty) { using var c = Open(); c.Execute("UPDATE Lockers SET IsFaulty = @f WHERE Id = @id", new { f = isFaulty ? 1 : 0, id }); }
    public void SetServiceLockers(int id1, int id2) { using var c = Open(); c.Execute("UPDATE Lockers SET IsService = 0"); c.Execute("UPDATE Lockers SET IsService = 1, EmployeeId = NULL WHERE Id IN (@id1, @id2)", new { id1, id2 }); }

    // === МЕТКИ ===
    public List<RfidTag> GetTags() { using var c = Open(); return c.Query<RfidTag>(@"SELECT t.Uid, t.EmployeeId, t.Description, e.FullName as OwnerName FROM RfidTags t JOIN Employees e ON e.Id = t.EmployeeId").ToList(); }
    public void AddTag(string uid, int employeeId) { using var c = Open(); c.Execute("INSERT OR REPLACE INTO RfidTags (Uid, EmployeeId, Description) VALUES (@u, @e, 'Основная')", new { u = uid, e = employeeId }); }
    public void DeleteTag(string uid) { using var c = Open(); c.Execute("DELETE FROM RfidTags WHERE Uid = @u", new { u = uid }); }

    // === ЛОГИ ===
    public void AddLog(int? empId, int? lockerId, string action, string details = "") { using var c = Open(); c.Execute("INSERT INTO Logs (EmployeeId, LockerId, Action, Details) VALUES (@e, @l, @a, @d)", new { e = empId, l = lockerId, a = action, d = details }); }
    public List<LogEntry> GetLogs() { using var c = Open(); return c.Query<LogEntry>(@"SELECT a.Id, a.Timestamp, e.FullName as EmployeeName, a.Action, a.Details as TagUid, a.LockerId FROM Logs a LEFT JOIN Employees e ON e.Id = a.EmployeeId ORDER BY a.Id DESC LIMIT 100").ToList(); }
}
