# Changelog

Формат: Keep a Changelog, SemVer.

## [0.8.0] - 2025-10-29 - UX и Dashboard P2

### Added
- Вкладка «Сводка»: 4 карточки (Сотрудники / Метки / Ячейки со свободными / Неисправно) + DataGrid последних 10 событий (#P2.1)
- Баннер Demo Mode в `AdminWindow` и `KioskWindow` — оранжевый, виден только при `DemoMode: true` (#P2.2)
- Toast-уведомления (`ToastHost`): fade-in 200мс, авто-скрытие 3с, стек до 3, цвета Success/Error/Info (#P2.3)
- Замена `MessageBox` на `Toasts.Show` в 4 местах: результат открытия сервисных, сохранение привязки, ошибки Modbus (openBtn, TestRelay)

### Changed
- Подтверждения (удаление сотрудника/метки, «снять всех», «открыть сервисные») остаются `MessageBox` — там нужен YesNo

## [0.7.0] - 2025-10-28 - Админ-панель P1

### Added
- Сортировка колонок в `EmpGrid` (`CanUserSortColumns`, `SortMemberPath=LockerDisplay`) (#P1.1)
- Фильтры сотрудников: Все / Без меток / Без ячейки (#P1.2)
- Поиск по номеру ячейки: `LockerSearchBox` + подсветка + карточка ячейки (#P1.3)
- `IHardware.IsModbusConnected` / `IsRfidAvailable` + реализация в `RealHardware`/`DemoHardware` (#P1.4.1)
- Индикатор статуса Modbus/RFID в верхней панели, обновление раз в 5 с, `DEMO` badge (#P1.4.2)
- Массовые операции: «Снять всех с ячеек» (`UnassignAllLockers`), «Открыть сервисные» (#P1.5)

### Changed
- `SessionTimer` и `StatusTimer` останавливаются в `LockSession`
- Тестов: 42 passed, 1 skipped (+3: `IsModbusConnected`, `IsRfidAvailable`, `UnassignAllLockers`)

## [0.6.0] - 2025-10-28 - Админ-панель P0

### Added
- Управление метками сотрудника в WPF-админке: список UID, добавление (`AddTagWindow` + сканирование 15с), удаление с подтверждением (#P0.1)
- Аудит всех админ-действий в `Database`: `ADMIN_ADD_EMPLOYEE`, `ADMIN_UPDATE_EMPLOYEE`, `ADMIN_DELETE_EMPLOYEE`, `ADMIN_ASSIGN_LOCKER`, `ADMIN_UPDATE_HARDWARE`, `ADMIN_SET_FAULTY`, `ADMIN_SET_SERVICE`, `ADMIN_ADD_TAG`, `ADMIN_DELETE_TAG`, `ADMIN_UPDATE_PIN` (#P0.2)
- Таймаут сессии 10 мин в `AdminWindow` + кнопка «🔒 Заблокировать»; индикатор обратного отсчёта (#P0.3)
- `oldPin` при смене PIN через `/api/settings/pin` — требуется знание текущего PIN, не только Bearer-токен (#P0.4)

### Changed
- Подтверждение удаления сотрудника: перечислены последствия (метки, привязка ячейки)
- `AdminWindow`: `EmpGrid_SelectionChanged` → `RenderEmployeeDetails()` для повторного рендера

### Fixed
- CA1305 в `Database.AssignLocker` (`ToString(InvariantCulture)`)
- CA0108: `AddTagWindow.Uid` → `TagUid` (не скрывать `UIElement.Uid`)

## [0.5.0] - 2025-10-27 - Качество

### Added
- TreatWarningsAsErrors=true
- .gitattributes (* text=auto eol=lf)
- Блокирующий Format check в CI

### Changed
- dotnet format по всему решению
- Убраны дубли Nullable/ImplicitUsings из csproj

### Fixed
- CA1305, CA1805, CA2201

## [0.4.0] - 2025-10-27 - Тестирование

### Added
- ShkafMFSB.Tests (xUnit): 39 тестов + 1 skip
- DatabaseTests (13), ApiValidationTests (20), DemoHardwareTests (6)
- Core/ApiValidation.cs (public)
- Database(string? dbPath)
- CI: Test + trx

### Fixed
- DemoHardware.StartRfid: утечка фонового цикла
- DemoHardware.WaitForNextTagAsync: гонка с timeout

## [0.3.0] - 2025-10-27 - Архитектура

### Added
- ShkafMFSB.Core (class library)
- IHardware + RealHardware + DemoHardware
- LockerConfig в appsettings.json
- AddEmployeeWindow

### Changed
- Hardware -> RealHardware(Config)
- DemoMode по умолчанию false
- PulseRelayAsync(module -> moduleId) (CA1716)

### Removed
- Демо-цикл из KioskWindow

## [0.2.0] - 2025-10-27 - Надёжность

### Added
- Retry с backoff в PulseRelayAsync -> Task<bool>
- Обработка провала открытия ячейки (SetState, 500, MessageBox)
- IDisposable для Hardware и WebServer, App.OnExit
- Ротация логов (10 МБ x 5)
- ContinueWith(OnlyOnFaulted) для HandleRequest

### Fixed
- Пустые catch -> Logger.LogError

## [0.1.0] - 2025-10-27 - Инфраструктура и безопасность

### Added
- CI (Build + Test + Format) на windows-latest
- .editorconfig, Directory.Build.props, .gitignore
- Branch protection main/develop
- PBKDF2-SHA256 для PIN + миграция legacy
- Bearer-токены (TTL 30 мин), 401
- CORS: только localhost/127.0.0.1
- Валидация JSON -> 400
- Logger.Redact + MaskUid
