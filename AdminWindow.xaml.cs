using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ShkafMFSB;

public partial class AdminWindow : Window
{
    private List<Locker> _lockers = new();
    private Locker? _selectedLocker;

    public AdminWindow() { InitializeComponent(); }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        if (PinBox.Password == "0000")
        {
            PinPanel.Visibility = Visibility.Collapsed;
            MainPanel.Visibility = Visibility.Visible;
            LoadData();
            BuildLockerMatrix();
        }
        else MessageBox.Show("Неверный PIN");
    }

    private void LoadData()
    {
        EmpGrid.ItemsSource = App.Db.GetEmployees(SearchBox.Text);
        _lockers = App.Db.GetLockers();
        HardwareGrid.ItemsSource = _lockers;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => LoadData();

    private void EmpGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EmpGrid.SelectedItem is not Employee emp) return;
        
        EmpDetailsPanel.Children.Clear();
        EmpDetailsPanel.Children.Add(new TextBlock { Text = emp.FullName, Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold });
        EmpDetailsPanel.Children.Add(new TextBlock { Text = $"Закреплённая ячейка: {emp.LockerDisplay}", Foreground = Brushes.White, Margin = new Thickness(0,10,0,0) });
        
        var btn = new Button { Content = "Отвязать метки", Background = new SolidColorBrush(Color.FromRgb(127,29,29)), Foreground = Brushes.White, Margin = new Thickness(0,20,0,0), Padding = new Thickness(10,5,10,5) };
        EmpDetailsPanel.Children.Add(btn);
    }

    private void BuildLockerMatrix()
    {
        LockerMatrix.Children.Clear();
        LockerMatrix.RowDefinitions.Clear();
        LockerMatrix.ColumnDefinitions.Clear();
        for (int i = 0; i < 17; i++) LockerMatrix.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        for (int i = 0; i < 4; i++) LockerMatrix.ColumnDefinitions.Add(new ColumnDefinition());

        var layout = GetLayout();
        foreach (var item in layout)
        {
            var l = _lockers.FirstOrDefault(x => x.Id == item.Id);
            if (l == null) continue;

            var btn = new Button { Tag = l, Margin = new Thickness(2), Padding = new Thickness(0) };
            btn.Click += (s, e) => { _selectedLocker = l; RenderLockerDetails(); };
            
            if (l.IsService) btn.Background = new SolidColorBrush(Color.FromRgb(180, 83, 9));
            else if (l.IsFaulty) btn.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29));
            else if (l.EmployeeId.HasValue) btn.Background = new SolidColorBrush(Color.FromRgb(30, 64, 175));
            else btn.Background = new SolidColorBrush(Color.FromRgb(51, 65, 85));

            btn.Content = new TextBlock { Text = l.DisplayText + (string.IsNullOrEmpty(l.OwnerName) ? "" : $"\n{l.OwnerName.Split(' ')[0]}"), Foreground = Brushes.White, TextAlignment = TextAlignment.Center, FontSize = 10 };

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

        LockerDetailsPanel.Children.Add(new TextBlock { Text = _selectedLocker.DisplayText, FontSize = 20, Foreground = Brushes.White, FontWeight = FontWeights.Bold });
        
        if (!_selectedLocker.IsService)
        {
            var combo = new ComboBox { Margin = new Thickness(0, 10, 0, 0), DisplayMemberPath = "FullName", SelectedValuePath = "Id" };
            combo.ItemsSource = App.Db.GetEmployees();
            combo.SelectedValue = _selectedLocker.EmployeeId;
            
            combo.SelectionChanged += (s, e) =>
            {
                int? newEmpId = combo.SelectedValue as int?;
                if (newEmpId == _selectedLocker.EmployeeId) return;

                if (newEmpId.HasValue)
                {
                    var existing = _lockers.FirstOrDefault(x => x.EmployeeId == newEmpId.Value);
                    if (existing != null) { MessageBox.Show($"Сотрудник уже закреплен за ячейкой №{existing.Id}."); combo.SelectedValue = _selectedLocker.EmployeeId; return; }
                }
                App.Db.AssignLocker(_selectedLocker.Id, newEmpId);
                LoadData(); BuildLockerMatrix();
            };
            LockerDetailsPanel.Children.Add(new TextBlock { Text = "Владелец:", Foreground = Brushes.Gray, Margin = new Thickness(0, 10, 0, 2) });
            LockerDetailsPanel.Children.Add(combo);
        }

        var openBtn = new Button { Content = "Открыть вручную", Margin = new Thickness(0, 20, 0, 0), Background = new SolidColorBrush(Color.FromRgb(30, 64, 175)), Foreground = Brushes.White, Padding = new Thickness(10, 5, 10, 5) };
        openBtn.Click += async (s, e) => await App.Hw.PulseRelayAsync(_selectedLocker.ModuleId, _selectedLocker.ChannelId);
        LockerDetailsPanel.Children.Add(openBtn);
    }

    private List<(int Id, int Col, int Row, int RowSpan)> GetLayout()
    {
        var service = _lockers.Where(x => x.IsService).ToList();
        var regular = _lockers.Where(x => !x.IsService).Select(x => x.Id).ToList();
        var list = new List<(int, int, int, int)>();
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

    private async void TestRelay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is Locker l) await App.Hw.PulseRelayAsync(l.ModuleId, l.ChannelId);
    }

    private void SaveHardware_Click(object sender, RoutedEventArgs e)
    {
        foreach (var l in _lockers) App.Db.UpdateHardware(l.Id, l.ModuleId, l.ChannelId);
        MessageBox.Show("Привязка сохранена.");
    }
}
