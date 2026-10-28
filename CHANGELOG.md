# Changelog

Формат: Keep a Changelog, SemVer.

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
