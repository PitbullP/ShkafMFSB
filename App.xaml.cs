using System.Windows;

namespace ShkafMFSB;

public partial class App : Application
{
    public static Database Db { get; private set; } = null!;
    public static Hardware Hw { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Db = new Database();
        Db.Initialize();
        
        Hw = new Hardware();
        Hw.ConnectModbus();
        Hw.StartRfid();
    }
}
