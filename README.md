# ShkafMFSB

WPF-приложение для шкафа выдачи RFID-меток (МФСБ): киоск для сотрудников, веб-панель администратора, управление Modbus-реле и PC/SC-считывателем.

## Возможности

- Киоск: RFID-метка -> открытие ячейки (или "Обратитесь к администратору")
- Админ-панель (WPF): сотрудники, метки, ячейки, тест реле, смена PIN, логи
- Веб-панель (браузер, 127.0.0.1): Bearer-токен после PIN
- Демо-режим: без оборудования

## Требования

- Windows 10/11, .NET SDK 8.0+
- Production: Modbus RTU-реле, PC/SC RFID-считыватель

## Быстрый старт

```bash
dotnet restore ShkafMFSB.sln
dotnet build ShkafMFSB.sln -c Release
dotnet test  ShkafMFSB.sln -c Release

---

## Команда 2/5 — `docs/architecture.md`

```bash
mkdir -p docs && cat > docs/architecture.md <<'EOF'
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

```csharp
var cfg = ConfigExtensions.Load();
Hw = cfg.DemoMode ? new DemoHardware(cfg) : new RealHardware(cfg);

---

## Команда 3/5 — `docs/security.md`

```bash
cat > docs/security.md <<'EOF'
# Безопасность ShkafMFSB

## PIN администратора

Хранение: Settings.AdminPin = "iterations.base64(salt).base64(hash)"

- PBKDF2-SHA256, 100 000 итераций
- Salt 16 байт, Hash 32 байта
- FixedTimeEquals

Первый запуск: PIN 0000 (сразу хешируется). Миграция legacy-БД: открытый -> хеш.

Ограничение: 4 цифры = 10^4. Для production >= 6 цифр + rate limiting.

## Web API - Bearer

POST /api/verify-pin -> TokenStore.Issue (32 байта base64url, TTL 30 мин).

- In-memory ConcurrentDictionary
- Отказ: 401 + {error:"Не авторизован"}
- При рестарте токены теряются

## CORS

Только http://localhost:{Port} и http://127.0.0.1:{Port}.
Access-Control-Allow-Origin только для разрешённого + Vary: Origin.
OPTIONS -> 204.

## Валидация

- V.Str, V.Number, V.NullableNumber, V.Bool
- JsonBodyReader.ReadObjectAsync

Правила: pin 4-128 (verify 0-128), name 1-200, id/lockerId/employeeId >= 1, uid 4-64.
ValidationException -> 400. JsonException -> 400.

## Логи

Redact вырезает pin/password/token/Bearer (GeneratedRegex). Ротация 10 МБ x 5.

## За рамками

- Rate limiting - нет
- Logout/отзыв токенов - нет
- TLS - только loopback
- Шифрование БД - нет

## Чек-лист развёртывания

- Сменить PIN 0000 на >= 6 цифр
- DemoMode: false
- Проверить Modbus.Port и Rfid.ReaderName
- Web.Port не конфликтует
- Права на shkaf_mfsb.db
