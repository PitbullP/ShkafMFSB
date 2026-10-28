using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShkafMFSB;

public partial class AdminWindow : Window
{
    private List<Locker> _lockers = new();
    private Locker? _selectedLocker;
    private Employee? _selectedEmployee;

    private const int SessionTimeoutMinutes = 10;
    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _lastActivity = DateTime.UtcNow;

    public AdminWindow()
    {
        InitializeComponent();
        _sessionTimer.Tick += (_, _) => OnSessionTick();

        // Любая активность продлевает сессию
        PreviewMouseMove += (_, _) => ResetActivity();
        PreviewMouseDown += (_, _) => ResetActivity();
        PreviewKeyDown += (_, _) => ResetActivity();
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        if (App.Db.VerifyPin(PinBox.Password))
        {
            PinBox.Clear();
            PinPanel.Visibility = Visibility.Collapsed;
            MainPanel.Visibility = Visibility.Visible;
            ResetActivity();
            _sessionTimer.Start();
            LoadData();
            BuildLockerMatrix();
        }
        else
        {
            PinBox.Clear();
            MessageBox.Show("Неверный PIN", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadData()
    {
        EmpGrid.ItemsSource = App.Db.GetEmployees(SearchBox.Text);
        _lockers = App.Db.GetLockers();
        HardwareGrid.ItemsSource = _lockers;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        LoadData();
    }

    // ═══════════════════════════════════════════════════════════════
    //  ВКЛАДКА: СОТРУДНИКИ
    // ═══════════════════════════════════════════════════════════════
    private void AddEmployee_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddEmployeeWindow { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.EmployeeName))
        {
            App.Db.AddEmployee(dlg.EmployeeName);
            LoadData();
        }
    }

    private void EmpGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedEmployee = EmpGrid.SelectedItem as Employee;
        RenderEmployeeDetails();
    }

    private void RenderEmployeeDetails()
    {
        if (_selectedEmployee == null) return;

        EmpDetailsPanel.Children.Clear();

        EmpDetailsPanel.Children.Add(new TextBlock { Text = _selectedEmployee.FullName, Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 15) });

        // Блок назначения ячейки
        EmpDetailsPanel.Children.Add(new TextBlock { Text = "Закреплённая ячейка:", Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)), Margin = new Thickness(0, 10, 0, 5) });

        var combo = new ComboBox { DisplayMemberPath = "DisplayText", SelectedValuePath = "Id", Margin = new Thickness(0, 0, 0, 15) };
        var availableLockers = _lockers.Where(l => !l.IsService && (!l.EmployeeId.HasValue || l.EmployeeId == _selectedEmployee.Id)).ToList();
        combo.ItemsSource = availableLockers;
        combo.SelectedValue = availableLockers.FirstOrDefault(l => l.EmployeeId == _selectedEmployee.Id)?.Id;

        combo.SelectionChanged += (s, ev) =>
        {
            int? newEmpId = _selectedEmployee.Id;
            int? newLockerId = combo.SelectedValue as int?;

            // Освобождаем старую
            var oldLocker = _lockers.FirstOrDefault(l => l.EmployeeId == newEmpId);
            if (oldLocker != null && oldLocker.Id != newLockerId)
            {
                App.Db.AssignLocker(oldLocker.Id, null);
            }

            // Назначаем новую
            if (newLockerId.HasValue)
            {
                var target = _lockers.FirstOrDefault(l => l.Id == newLockerId.Value);
                if (target != null && target.EmployeeId != newEmpId)
                {
                    App.Db.AssignLocker(newLockerId.Value, newEmpId);
                }
            }

            LoadData();
            BuildLockerMatrix();
            // Обновляем список, чтобы не слетело выделение
            var currentId = _selectedEmployee.Id;
            EmpGrid.SelectedItem = ((List<Employee>)EmpGrid.ItemsSource).FirstOrDefault(emp => emp.Id == currentId);
        };

        EmpDetailsPanel.Children.Add(combo);

        // === Метки сотрудника ===
        EmpDetailsPanel.Children.Add(new TextBlock { Text = "Метки сотрудника:", Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)), Margin = new Thickness(0, 10, 0, 5) });

        var myTags = App.Db.GetTags().Where(t => t.EmployeeId == _selectedEmployee.Id).ToList();
        if (myTags.Count == 0)
        {
            EmpDetailsPanel.Children.Add(new TextBlock { Text = "Нет зарегистрированных меток", Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)), FontStyle = FontStyles.Italic, Margin = new Thickness(0, 0, 0, 5) });
        }
        else
        {
            foreach (var tag in myTags)
            {
                var tagRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                tagRow.Children.Add(new TextBlock { Text = tag.Uid, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Width = 200, FontFamily = new FontFamily("Consolas") });
                var delTagBtn = new Button { Content = "Удалить", Style = (Style)FindResource("DangerBtn"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(8, 0, 0, 0) };
                var tagUid = tag.Uid;
                delTagBtn.Click += (s, ev) =>
                {
                    if (MessageBox.Show($"Удалить метку {tagUid}?\nСотрудник {_selectedEmployee.FullName} не сможет открыть ячейку этой меткой.", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    {
                        App.Db.DeleteTag(tagUid);
                        RenderEmployeeDetails();
                    }
                };
                tagRow.Children.Add(delTagBtn);
                EmpDetailsPanel.Children.Add(tagRow);
            }
        }

        var addTagBtn = new Button { Content = "+ Добавить метку", Style = (Style)FindResource("PrimaryBtn"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        addTagBtn.Click += (s, ev) =>
        {
            var dlg = new AddTagWindow { Owner = this };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.TagUid))
            {
                try
                {
                    App.Db.AddTag(dlg.TagUid!, _selectedEmployee.Id);
                    RenderEmployeeDetails();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось добавить метку: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        };
        EmpDetailsPanel.Children.Add(addTagBtn);

        // Кнопки управления
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
        var delBtn = new Button { Content = "Удалить сотрудника", Style = (Style)FindResource("DangerBtn") };
        delBtn.Click += (s, ev) =>
        {
            if (MessageBox.Show($"Удалить сотрудника {_selectedEmployee.FullName}?\n\nБудут удалены:\n- все метки сотрудника\n- привязка к ячейке (ячейка станет свободной)", "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                App.Db.DeleteEmployee(_selectedEmployee.Id);
                LoadData();
                EmpDetailsPanel.Children.Clear();
                EmpDetailsPanel.Children.Add(new TextBlock { Text = "Сотрудник удалён. Выберите другого.", Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 100, 0, 0) });
            }
        };
        btnRow.Children.Add(delBtn);
        EmpDetailsPanel.Children.Add(btnRow);
    }

    // ═══════════════════════════════════════════════════════════════
    //  ВКЛАДКА: ЯЧЕЙКИ
    // ═══════════════════════════════════════════════════════════════
    private void BuildLockerMatrix()
    {
        LockerMatrix.Children.Clear();
        LockerMatrix.RowDefinitions.Clear();
        LockerMatrix.ColumnDefinitions.Clear();

        for (int i = 0; i < 17; i++) LockerMatrix.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });
        for (int i = 0; i < 4; i++) LockerMatrix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var layout = GetLayout();
        foreach (var item in layout)
        {
            var l = _lockers.FirstOrDefault(x => x.Id == item.Id);
            if (l == null) continue;

            var btn = new Button { Tag = l, Margin = new Thickness(2), Padding = new Thickness(0), BorderThickness = new Thickness(2) };
            btn.Click += (s, e) => { _selectedLocker = l; RenderLockerDetails(); };

            if (l.IsService) btn.Background = new SolidColorBrush(Color.FromRgb(180, 83, 9));
            else if (l.IsFaulty) btn.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29));
            else if (l.EmployeeId.HasValue) btn.Background = new SolidColorBrush(Color.FromRgb(30, 64, 175));
            else btn.Background = new SolidColorBrush(Color.FromRgb(51, 65, 85));

            if (_selectedLocker?.Id == l.Id) btn.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            else btn.BorderBrush = Brushes.Transparent;

            btn.Content = new TextBlock
            {
                Text = l.DisplayText + (string.IsNullOrEmpty(l.OwnerName) ? "" : $"\n{l.OwnerName.Split(' ')[0]}"),
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                FontSize = l.IsService ? 10 : 12,
                FontWeight = FontWeights.Bold
            };

            Grid.SetColumn(btn, item.Col);
            Grid.SetRow(btn, item.Row);
            Grid.SetRowSpan(btn, item.RowSpan);
            LockerMatrix.Children.Add(btn);
        }
    }

    private void RenderLockerDetails()
    {
        LockerDetailsPanel.Children.Clear();
        if (_selectedLocker == null) return;

        LockerDetailsPanel.Children.Add(new TextBlock { Text = _selectedLocker.DisplayText, FontSize = 22, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });

        if (!_selectedLocker.IsService)
        {
            LockerDetailsPanel.Children.Add(new TextBlock { Text = "Владелец:", Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)), Margin = new Thickness(0, 10, 0, 5) });

            var combo = new ComboBox { DisplayMemberPath = "FullName", SelectedValuePath = "Id", Margin = new Thickness(0, 0, 0, 15) };
            var emps = App.Db.GetEmployees();
            combo.ItemsSource = emps;
            combo.SelectedValue = _selectedLocker.EmployeeId;

            combo.SelectionChanged += (s, e) =>
            {
                int? newEmpId = combo.SelectedValue as int?;
                if (newEmpId == _selectedLocker.EmployeeId) return;

                if (newEmpId.HasValue)
                {
                    var existing = _lockers.FirstOrDefault(x => x.EmployeeId == newEmpId.Value);
                    if (existing != null)
                    {
                        MessageBox.Show($"Сотрудник уже закреплен за ячейкой №{existing.Id}. Освободите её сначала.");
                        combo.SelectedValue = _selectedLocker.EmployeeId;
                        return;
                    }
                }
                App.Db.AssignLocker(_selectedLocker.Id, newEmpId);
                LoadData();
                BuildLockerMatrix();
                RenderLockerDetails();
            };
            LockerDetailsPanel.Children.Add(combo);
        }

        var openBtn = new Button { Content = "Открыть вручную (Тест)", Margin = new Thickness(0, 20, 0, 0), Style = (Style)FindResource("PrimaryBtn") };
        openBtn.Click += async (s, e) =>
        {
            if (_selectedLocker == null) return;
            bool ok = await App.Hw.PulseRelayAsync(_selectedLocker.ModuleId, _selectedLocker.ChannelId);
            if (!ok) MessageBox.Show($"Не удалось открыть ячейку № {_selectedLocker.Id} (проверьте Modbus-связь)", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        LockerDetailsPanel.Children.Add(openBtn);
    }

    private List<(int Id, int Col, int Row, int RowSpan)> GetLayout()
    {
        var service = _lockers.Where(x => x.IsService).ToList();
        var regular = _lockers.Where(x => !x.IsService).Select(x => x.Id).ToList();
        var list = new List<(int Id, int Col, int Row, int RowSpan)>();
        int rIdx = 0;

        list.Add((regular[rIdx++], 0, 0, 1)); list.Add((regular[rIdx++], 0, 1, 1)); list.Add((regular[rIdx++], 0, 2, 1));
        if (service.Count > 0) list.Add((service[0].Id, 0, 3, 4));
        for (int r = 7; r < 17; r++) list.Add((regular[rIdx++], 0, r, 1));

        for (int r = 0; r < 17; r++) list.Add((regular[rIdx++], 1, r, 1));

        list.Add((regular[rIdx++], 2, 0, 1)); list.Add((regular[rIdx++], 2, 1, 1)); list.Add((regular[rIdx++], 2, 2, 1));
        if (service.Count > 1) list.Add((service[1].Id, 2, 3, 4));
        for (int r = 7; r < 17; r++) list.Add((regular[rIdx++], 2, r, 1));

        for (int r = 0; r < 17; r++) list.Add((regular[rIdx++], 3, r, 1));

        return list;
    }

    // ═══════════════════════════════════════════════════════════════
    //  ВКЛАДКА: НАСТРОЙКИ
    // ═══════════════════════════════════════════════════════════════
    private async void TestRelay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is Locker l)
        {
            bool ok = await App.Hw.PulseRelayAsync(l.ModuleId, l.ChannelId);
            if (!ok) MessageBox.Show($"Не удалось открыть ячейку № {l.Id} (проверьте Modbus-связь)", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveHardware_Click(object sender, RoutedEventArgs e)
    {
        foreach (var l in _lockers) App.Db.UpdateHardware(l.Id, l.ModuleId, l.ChannelId);
        MessageBox.Show("Привязка оборудования сохранена в базе данных.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // === Сессия: таймаут и блокировка ===

    private void ResetActivity()
    {
        _lastActivity = DateTime.UtcNow;
    }

    private void OnSessionTick()
    {
        var elapsed = DateTime.UtcNow - _lastActivity;
        var remaining = TimeSpan.FromMinutes(SessionTimeoutMinutes) - elapsed;

        if (remaining <= TimeSpan.Zero)
        {
            LockSession();
            return;
        }

        SessionTimerText.Text = $"⏱ Сессия: {remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    private void LockSession()
    {
        _sessionTimer.Stop();
        MainPanel.Visibility = Visibility.Collapsed;
        PinPanel.Visibility = Visibility.Visible;
        PinBox.Clear();
        PinBox.Focus();

        EmpDetailsPanel.Children.Clear();
        LockerDetailsPanel.Children.Clear();
        SessionTimerText.Text = "";
        _selectedEmployee = null;
        _selectedLocker = null;
    }

    private void LockBtn_Click(object sender, RoutedEventArgs e)
    {
        LockSession();
    }
}
