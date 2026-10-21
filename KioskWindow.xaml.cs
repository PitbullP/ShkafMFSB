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
            
            // 1. Отключаем режим "поверх всех окон" и возвращаем курсор
            this.Topmost = false;
            this.Cursor = Cursors.Arrow;
            
            // 2. Открываем админку
            var admin = new AdminWindow();
            admin.Owner = this; // Делаем киоск родителем, чтобы админка была по центру
            admin.ShowDialog();
            
            // 3. Когда админка закрылась — возвращаем киоск-режим
            this.Topmost = true;
            this.Cursor = Cursors.None;
        }
    }
