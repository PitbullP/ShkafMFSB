using System;
using System.Windows;

namespace ShkafMFSB;

public partial class AddTagWindow : Window
{
    public string? TagUid { get; private set; }

    public AddTagWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UidBox.Focus();
    }

    private async void ScanBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UidBox.IsEnabled = false;
            ScanBtn.IsEnabled = false;
            ScanBtn.Content = "Сканирование...";
            HintLabel.Content = "Приложите метку к считывателю (до 15 сек)";

            var uid = await App.Hw.WaitForNextTagAsync(15000);
            UidBox.Text = uid;
            HintLabel.Content = "Метка считана";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось считать метку: {ex.Message}\n\nВведите UID вручную.", "Ошибка сканирования", MessageBoxButton.OK, MessageBoxImage.Warning);
            HintLabel.Content = "Введите UID вручную";
        }
        finally
        {
            ScanBtn.Content = "Сканировать";
            ScanBtn.IsEnabled = true;
            UidBox.IsEnabled = true;
        }
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        var uid = UidBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(uid))
        {
            MessageBox.Show("Введите UID метки", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (uid.Length < 4 || uid.Length > 64)
        {
            MessageBox.Show("UID должен быть от 4 до 64 символов", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Разрешаем только hex-символы (как у реальных RFID-меток), но не навязываем
        foreach (var c in uid)
        {
            if (!Uri.IsHexDigit(c))
            {
                if (MessageBox.Show($"UID содержит не-hex символ '{c}'. Всё равно добавить?", "Внимание", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                break;
            }
        }

        TagUid = uid.ToUpperInvariant();
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
