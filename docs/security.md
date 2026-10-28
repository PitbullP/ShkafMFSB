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
