namespace ShkafMFSB;

public class Employee
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string LockerDisplay { get; set; } = "";
    public int TagCount { get; set; }
}

public class Locker
{
    public int Id { get; set; }
    public int? EmployeeId { get; set; }
    public bool IsService { get; set; }
    public bool IsFaulty { get; set; }
    public int ModuleId { get; set; }
    public int ChannelId { get; set; }
    public string OwnerName { get; set; } = "";
    public string DisplayText => IsService ? "Сервисная" : $"№ {Id}";
}

public class RfidTag
{
    public string Uid { get; set; } = "";
    public int EmployeeId { get; set; }
    public string Description { get; set; } = "";
    public string OwnerName { get; set; } = "";
}

public class LogEntry
{
    public long Id { get; set; }
    public string Timestamp { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public string Action { get; set; } = "";
    public string LockerDisplay { get; set; } = "";
    public string TagUid { get; set; } = "";
}
