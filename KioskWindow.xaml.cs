using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ShkafMFSB;

public partial class KioskWindow : Window
{
    private int _adminClicks;
    private DateTime _firstClickTime;
    private string _lastUid = "";
    private DateTime _lastScanTime = DateTime.MinValue;

    public KioskWindow()
    {
        InitializeComponent();
        App.MainKiosk = this;
        App.Hw.TagScanned += OnTagScanned;
    }

    private async void OnTagScanned(string uid)
    {
        if (uid == _lastUid && (DateTime.Now - _lastScanTime).TotalSeconds < 3) return;
        _lastUid = uid; _lastScanTime = DateTime.Now;

        SetState("reading", "Считывание…", "", "Icon.Reading", "#1E40AF");
        await Task.Delay(400);

        var tags = App.Db.GetTags();
        var tag = tags.FirstOrDefault(t => t.Uid == uid);

        if (tag == null)
        {
            App.Db.AddLog(null, null, "DENIED_UNKNOWN_TAG", uid);
            SetState("denied", "Метка не зарегистрирована", "Обратитесь к администратору", "Icon.Denied", "#B01E1E");
            await Task.Delay(4000);
        }
        else
        {
            var employees = App.Db.GetEmployees();
            var emp = employees.FirstOrDefault(e => e.Id == tag.EmployeeId);
            
            if (emp == null)
            {
                App.Db.AddLog(tag.EmployeeId, null, "DENIED_NO_EMPLOYEE", uid);
                SetState("denied", "Сотрудник не найден", "Обратитесь к администратору", "Icon.Denied", "#B01E1E");
                await Task.Delay(4000);
            }
            else
            {
                var lockers = App.Db.GetLockers();
                var locker = lockers.FirstOrDefault(l => l.EmployeeId == emp.Id);

                if (locker == null)
                {
                    App.Db.AddLog(emp.Id, null, "DENIED_NO_LOCKER", uid);
                    SetState("denied", emp.FullName, "За вами не закреплена ячейка", "Icon.Warning", "#C06A00");
                    await Task.Delay(4000);
                }
                else
                {
                    bool opened = await App.Hw.PulseRelayAsync(locker.ModuleId, locker.ChannelId);
                    if (opened)
                    {
                        App.Db.AddLog(emp.Id, locker.Id, "GRANTED", uid);
                        SetState("granted", emp.FullName, $"Ячейка № {locker.Id} открыта", "Icon.Success", "#1B7A3D");
                        await Task.Delay(5000);
                    }
                    else
                    {
                        App.Db.AddLog(emp.Id, locker.Id, "OPEN_FAILED", uid);
                        SetState("denied", emp.FullName, "Не удалось открыть ячейку. Обратитесь к администратору", "Icon.Denied", "#B01E1E");
                        await Task.Delay(4000);
                    }
                }
            }
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
            string url = $"http://localhost:{App.Server.Port}";
            this.WindowState = WindowState.Minimized;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); 
        }
    }
}
