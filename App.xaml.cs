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
        // Глобальный перехват всех ошибок
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Logger.LogError("AppDomain", ex ?? new Exception("Неизвестная ошибка"));
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Logger.LogError("UI Thread", args.Exception);
            MessageBox.Show($"Критическая ошибка:\n{args.Exception.Message}\n\nПодробности в файле app.log", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            Logger.Log("=== ЗАПУСК ПРОГРАММЫ ===");
            base.OnStartup(e);
            
            Logger.Log("Инициализация базы данных...");
            Db = new Database();
            Db.Initialize();
            Logger.Log("База данных успешно загружена.");
            
            Logger.Log("Инициализация оборудования (Modbus/RFID)...");
            Hw = new Hardware();
            Hw.ConnectModbus();
            Hw.StartRfid();
            Logger.Log("Оборудование инициализировано.");

            Logger.Log("Запуск веб-сервера...");
            Server = new WebServer(Db, Hw);
            Server.Start();
            Logger.Log("Веб-сервер запущен. Программа готова к работе.");
        }
        catch (Exception ex)
        {
            Logger.LogError("Запуск (OnStartup)", ex);
            MessageBox.Show($"Не удалось запустить программу:\n{ex.Message}\n\nПодробности сохранены в файл app.log", "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
