using System.Windows;

namespace ShkafMFSB;

public partial class App : Application
{
    public static Database Db { get; private set; } = null!;
    public static Hardware Hw { get; private set; } = null!;
    public static WebServer Server { get; private set; } = null!;
    public static KioskWindow? MainKiosk { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Logger.LogError("AppDomain", ex ?? new Exception("Неизвестная ошибка"));
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Logger.LogError("UI Thread", args.Exception);
            args.Handled = true;
        };

        try
        {
            Logger.Log("=== ЗАПУСК ПРОГРАММЫ ===");
            base.OnStartup(e);
            
            Hw = new Hardware();

            Db = new Database();
            Db.Initialize(Hw.Settings.LockerConfig);

            Hw.ConnectModbus();
            Hw.StartRfid();

            Server = new WebServer(Db, Hw);
            Server.Start();
        }
        catch (Exception ex)
        {
            Logger.LogError("Запуск (OnStartup)", ex);
            MessageBox.Show($"Не удалось запустить программу:\n{ex.Message}\n\nПодробности в app.log", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Server?.Dispose(); }
        catch (Exception ex) { Logger.LogError("App.OnExit (Server)", ex); }
        try { Hw?.Dispose(); }
        catch (Exception ex) { Logger.LogError("App.OnExit (Hardware)", ex); }
        Logger.Log("=== ЗАВЕРШЕНИЕ ПРОГРАММЫ ===");
        base.OnExit(e);
    }
}
