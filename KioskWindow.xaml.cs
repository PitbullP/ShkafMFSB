using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ShkafMFSB;

public partial class KioskWindow : Window
{
    private int _adminClicks = 0;
    private DateTime _firstClickTime;
    private string _lastUid = "";
    private DateTime _lastScanTime = DateTime.MinValue;

    public KioskWindow()
    {
        InitializeComponent();
        App.Hw.TagScanned += OnTagScanned;
        
        if (App.Hw.Settings.DemoMode)
        {
            Task.Run(async () => {
                await Task.Delay(3000);
                while (true)
                {
                    await Task.Delay(6000);
                    Dispatcher.Invoke(() => OnTagScanned("A1B2C3D4"));
                }
            });
        }
    }

    private async void OnTagScanned(string uid)
    {
        if (uid == _lastUid && (DateTime.Now - _lastScanTime).TotalSeconds < 3) return;
        _lastUid = uid; _lastScanTime = DateTime.Now;

        SetState("reading", "Считывание…", "", "Icon.Reading", "#1E40AF");
        await Task.Delay(400);

        if (uid == "A1B2C3D4")
        {
            SetState("granted", "Иванов Иван Иванович", "Ячейка № 12 открыта", "Icon.Success", "#1B7A3D");
            await App.Hw.PulseRelayAsync(1, 11);
            await Task.Delay(5000);
        }
        else
        {
            SetState("denied", "Метка не зарегистрирована", "Обратитесь к администратору", "Icon.Denied", "#B01E1E");
            await Task.Delay(4000);
        }

        SetState("idle", "Приложите карту к считывателю", "", "Icon.Idle", "#0B2A4A");
    }

    private void SetState(string state, string main, string sub, string iconKey, string bg)
    {
        Dispatcher.Invoke(() =>
        {
            RootGrid.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
            TxtMain.Text = main;
            TxtSub.Text = sub;
            TxtSub.Visibility = string.IsNullOrEmpty(sub) ? Visibility.Collapsed : Visibility.Visible;
            IconPath.Data = (Geometry)Application.Current.Resources[iconKey];
        });
    }

    private void AdminZone_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_adminClicks == 0 || (DateTime.Now - _firstClickTime).TotalSeconds > 3)
        {
            _adminClicks = 1; _firstClickTime = DateTime.Now; return;
        }
        _adminClicks++;
        if (_adminClicks >= 5)
        {
            _adminClicks = 0;
            var admin = new AdminWindow();
            admin.ShowDialog();
        }
    }
}
