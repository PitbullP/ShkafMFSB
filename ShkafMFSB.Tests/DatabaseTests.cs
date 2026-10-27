using Microsoft.Data.Sqlite;
using ShkafMFSB;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ShkafMFSB.Tests;

/// <summary>Временная БД: уникальный путь, удаление файлов в Dispose.</summary>
public sealed class TempDb : IDisposable
{
    public string Path { get; }
    public Database Db { get; }

    public TempDb()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"shkaf_test_{Guid.NewGuid():N}.db");
        Db = new Database(Path);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { Path, Path + "-wal", Path + "-shm" })
            if (File.Exists(f)) { try { File.Delete(f); } catch { /* best-effort */ } }
    }

    public string? ReadRawSetting(string key)
    {
        using var c = new SqliteConnection($"Data Source={Path}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Value FROM Settings WHERE Key = @k";
        cmd.Parameters.AddWithValue("@k", key);
        return cmd.ExecuteScalar() as string;
    }
}

public class DatabaseTests
{
    // ---------- Инициализация / LockerConfig ----------

    [Fact]
    public void Initialize_DefaultConfig_Creates62Lockers()
    {
        using var t = new TempDb();
        t.Db.Initialize();
        var lockers = t.Db.GetLockers();
        Assert.Equal(62, lockers.Count);
        Assert.Equal(2, lockers.Count(l => l.IsService)); // id 61, 62
        Assert.All(lockers.Where(l => l.Id <= 32), l => Assert.Equal(1, l.ModuleId));
        Assert.All(lockers.Where(l => l.Id > 32), l => Assert.Equal(2, l.ModuleId));
    }

    [Fact]
    public void Initialize_CustomConfig_CreatesRequestedCells()
    {
        using var t = new TempDb();
        var cfg = new LockerConfig { TotalCells = 10, ServiceCellThreshold = 8, Module1MaxCell = 5 };
        t.Db.Initialize(cfg);
        var lockers = t.Db.GetLockers();
        Assert.Equal(10, lockers.Count);
        Assert.Equal(2, lockers.Count(l => l.IsService)); // id 9, 10
        Assert.Equal(1, lockers.First(l => l.Id == 5).ModuleId);
        Assert.Equal(2, lockers.First(l => l.Id == 6).ModuleId);
    }

    [Fact]
    public void Initialize_InvalidConfig_FallsBackToDefaults()
    {
        using var t = new TempDb();
        t.Db.Initialize(new LockerConfig { TotalCells = 0, ServiceCellThreshold = -5, Module1MaxCell = 999 });
        var lockers = t.Db.GetLockers();
        Assert.Equal(62, lockers.Count);
    }

    // ---------- PIN / PBKDF2 / миграция ----------

    [Fact]
    public void DefaultPin_Is0000_AndVerifies()
    {
        using var t = new TempDb();
        t.Db.Initialize();
        Assert.True(t.Db.VerifyPin("0000"));
        Assert.False(t.Db.VerifyPin("1234"));
        Assert.False(t.Db.VerifyPin(""));
    }

    [Fact]
    public void StoredPin_IsHashed_NotPlaintext()
    {
        using var t = new TempDb();
        t.Db.Initialize();
        var stored = t.ReadRawSetting("AdminPin");
        Assert.NotNull(stored);
        Assert.NotEqual("0000", stored);
        // формат: iterations.salt.hash
        Assert.Equal(3, stored!.Split('.').Length);
    }

    [Fact]
    public void UpdatePin_ChangesVerification()
    {
        using var t = new TempDb();
        t.Db.Initialize();
        t.Db.UpdatePin("4321");
        Assert.True(t.Db.VerifyPin("4321"));
        Assert.False(t.Db.VerifyPin("0000"));
    }

    [Fact]
    public void UpdatePin_Empty_Throws()
    {
        using var t = new TempDb();
        t.Db.Initialize();
        Assert.Throws<ArgumentException>(() => t.Db.UpdatePin(""));
        Assert.Throws<ArgumentException>(() => t.Db.UpdatePin("   "));
    }

    [Fact]
    public void LegacyPlaintextPin_MigratedToHashOnInitialize()
    {
        using var t = new TempDb();
        // 1) Создаём БД с хешем, затем вручную перезаписываем открытым PIN (как было до 1.1)
        t.Db.Initialize();
        using (var c = new SqliteConnection($"Data Source={t.Path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE Settings SET Value = '0000' WHERE Key = 'AdminPin'";
            cmd.ExecuteNonQuery();
        }
        Assert.Equal("0000", t.ReadRawSetting("AdminPin"));

        // 2) Повторная инициализация должна захешировать legacy-значение
        new Database(t.Path).Initialize();

        var stored = t.ReadRawSetting("AdminPin");
        Assert.NotEqual("0000", stored);
        Assert.Equal(3, stored!.Split('.').Length);
        Assert.True(t.Db.VerifyPin("0000"));
    }

    // ---------- CRUD ----------

    [Fact]
    public void AddEmployee_And_GetEmployees()
    {
        using var t = new TempDb();
        t.Db.Initialize();
        t.Db.AddEmployee("Иванов Иван");
        t.Db.AddEmployee("Петров Пётр");

        var list = t.Db.GetEmployees();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, e => e.FullName == "Иванов Иван");
    }

    [Fact]
    public void AddTag_And_GetTags()
    {
        using var t = new TempDb();
        t.Db.Initialize();
        t.Db.AddEmployee("Сидоров");
        var empId = t.Db.GetEmployees().First().Id;

        t.Db.AddTag("A1B2C3D4", empId);
        var tags = t.Db.GetTags();
        Assert.Single(tags);
        Assert.Equal("A1B2C3D4", tags[0].Uid);
        Assert.Equal(empId, tags[0].EmployeeId);
    }

    [Fact]
    public void AssignLocker_UpdatesEmployeeId()
    {
        using var t = new TempDb();
        t.Db.Initialize();
        t.Db.AddEmployee("Тестов");
        var empId = t.Db.GetEmployees().First().Id;

        t.Db.AssignLocker(5, empId);
        var locker = t.Db.GetLockers().First(l => l.Id == 5);
        Assert.Equal(empId, locker.EmployeeId);

        t.Db.AssignLocker(5, null);
        locker = t.Db.GetLockers().First(l => l.Id == 5);
        Assert.Null(locker.EmployeeId);
    }

    [Fact]
    public void SetFaulty_And_SetServiceLockers()
    {
        using var t = new TempDb();
        t.Db.Initialize();

        t.Db.SetFaulty(3, true);
        Assert.True(t.Db.GetLockers().First(l => l.Id == 3).IsFaulty);

        // По умолчанию сервисные — 61, 62; переназначаем на 1, 2
        t.Db.SetServiceLockers(1, 2);
        var lockers = t.Db.GetLockers();
        Assert.Equal(2, lockers.Count(l => l.IsService));
        Assert.True(lockers.First(l => l.Id == 1).IsService);
        Assert.True(lockers.First(l => l.Id == 2).IsService);
        Assert.False(lockers.First(l => l.Id == 61).IsService);
    }

    [Fact]
    public void AddLog_And_GetLogs()
    {
        using var t = new TempDb();
        t.Db.Initialize();
        t.Db.AddLog(null, null, "TEST_ACTION", "details");
        var logs = t.Db.GetLogs();
        Assert.Single(logs);
        Assert.Equal("TEST_ACTION", logs[0].Action);
    }
}
