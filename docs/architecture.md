# Архитектура ShkafMFSB

## Слои

- ShkafMFSB (net8.0-windows): WPF + WebServer
- ShkafMFSB.Core (net8.0): Database, IHardware, Logger, ApiValidation, Models
- ShkafMFSB.Tests (net8.0): xUnit, 39 тестов

## Поток: сканирование -> открытие

1. KioskWindow подписан на App.Hw.TagScanned
2. IHardware.StartRfid: фоновое чтение (PC/SC или эмуляция)
3. OnTagScanned: поиск RfidTag -> Employee -> Locker, логирование
4. PulseRelayAsync(moduleId, channel): retry 3x с backoff 100/300/900 мс
5. true -> SetState(granted) + GRANTED; false -> denied + OPEN_FAILED

## Web API

1. POST /api/verify-pin -> TokenStore.Issue (32 байта base64url, TTL 30 мин)
2. Все эндпоинты кроме verify-pin и OPTIONS -> Bearer
3. POST /api/open -> PulseRelayAsync false -> 500

## Выбор IHardware

var cfg = ConfigExtensions.Load();
Hw = cfg.DemoMode ? new DemoHardware(cfg) : new RealHardware(cfg);

## Схема БД (SQLite)

- Employees(Id PK, FullName)
- Lockers(Id PK, EmployeeId, IsService, IsFaulty, ModuleId, ChannelId)
- RfidTags(Uid PK, EmployeeId, Description)
- Logs(Id PK, Timestamp, EmployeeId, LockerId, Action, Details)
- Settings(Key PK, Value)

Database.Initialize(LockerConfig?): создаёт таблицы, PIN 0000 (хеш), N ячеек.
Legacy-миграция: открытый PIN -> хеш.

## Границы

- Core: net8.0, без WPF/HttpListener/System.Windows
- App: всё WPF-специфичное
- Tests: только Core
