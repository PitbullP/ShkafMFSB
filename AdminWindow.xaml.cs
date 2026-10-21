using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShkafMFSB;

public partial class AdminWindow : Window
{
    private List<Locker> _lockers = new();
    private Locker? _selectedLocker;
    private Employee? _selectedEmployee;

    public AdminWindow()
    {
        InitializeComponent();
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        if (PinBox.Password == "0000")
        {
            PinPanel.Visibility = Visibility.Collapsed;
            MainPanel.Visibility = Visibility.Visible;
            LoadData();
            BuildLockerMatrix();
        }
        else
        {
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
        // Простой ввод ФИО (для теста)
        var dialog = new Window
        {
            Title = "Новый сотрудник", Width = 300, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, Background = new SolidColorBrush(Color.FromRgb(15, 23, 42))
        };
        var grid = new Grid { Margin = new Thickness(15) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var txt = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
        var btn = new Button { Content = "Создать", Background = new SolidColorBrush(Color.FromRgb(30, 64, 175)), Foreground = Brushes.White, Padding = new Thickness(10, 5, 10, 5) };
        
        Grid.SetRow(txt, 0); grid.Children.Add(txt);
        Grid.SetRow(btn, 1); grid.Children.Add(btn);
        dialog.Content = grid;

        btn.Click += (s, ev) =>
        {
            if (!string.IsNullOrWhiteSpace(txt.Text))
            {
                App.Db.AddEmployee(txt.Text.Trim());
                LoadData();
                dialog.Close();
            }
        };
        dialog.ShowDialog();
    }

    private void EmpGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedEmployee = EmpGrid.SelectedItem as Employee;
        if (_selectedEmployee == null) return;
        
        EmpDetailsPanel.Children.Clear();
        
        EmpDetailsPanel.Children.Add(new TextBlock { Text = _selectedEmployee.FullName, Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,15) });
        
        // Блок назначения ячейки
        EmpDetailsPanel.Children.Add(new TextBlock { Text = "Закреплённая ячейка:", Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)), Margin = new Thickness(0, 10, 0, 5) });
        
        var combo = new ComboBox { DisplayMemberPath = "DisplayText", SelectedValuePath = "Id", Margin = new Thickness(0,0,0,15) };
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

        // Кнопки управления
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
        var delBtn = new Button { Content = "Удалить сотрудника", Style = (Style)FindResource("DangerBtn") };
        delBtn.Click += (s, ev) => {
            if (MessageBox.Show("Удалить сотрудника?", "Подтверждение", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                App.Db.DeleteEmployee(_selectedEmployee.Id);
                LoadData();
                EmpDetailsPanel.Children.Clear();
                EmpDetailsPanel.Children.Add(new TextBlock { Text = "Сотрудник удалён. Выберите другого.", Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,100,0,0) });
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

            btn.Content = new TextBlock { 
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

        LockerDetailsPanel.Children.Add(new TextBlock { Text = _selectedLocker.DisplayText, FontSize = 22, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,10) });
        
        if (!_selectedLocker.IsService)
        {
            LockerDetailsPanel.Children.Add(new TextBlock { Text = "Владелец:", Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)), Margin = new Thickness(0, 10, 0, 5) });
            
            var combo = new ComboBox { DisplayMemberPath = "FullName", SelectedValuePath = "Id", Margin = new Thickness(0,0,0,15) };
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
        openBtn.Click += async (s, e) => await App.Hw.PulseRelayAsync(_selectedLocker.ModuleId, _selectedLocker.ChannelId);
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
            await App.Hw.PulseRelayAsync(l.ModuleId, l.ChannelId);
    }

    private void SaveHardware_Click(object sender, RoutedEventArgs e)
    {
        foreach (var l in _lockers) App.Db.UpdateHardware(l.Id, l.ModuleId, l.ChannelId);
        MessageBox.Show("Привязка оборудования сохранена в базе данных.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
