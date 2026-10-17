# API endpoints

Базовый URL: `http://localhost:5000`
Все защищённые эндпоинты требуют header `X-Api-Key: <ключ>`.
`/health` — **без** ключа.

## Health

### GET /health
```json
{ "status": "ok", "time": "2026-10-08T00:00:00+00:00" }
```

## Майнеры и ватчдоги

### GET /api/v1/miners
Список всех майнеров с последним снапшотом.
```json
[
  {
    "minerId": "02:A5:5D:B1:D4:C0",
    "ip": "192.168.2.101",
    "mac": "02:A5:5D:B1:D4:C0",
    "hostname": "Antminer",
    "model": "Antminer S19k Pro",
    "modelCode": "s19kpro",
    "firmware": "VNISH 1.2.7",
    "watchdogId": "farm-local-01",
    "firstSeen": "...",
    "lastSeen": "...",
    "snapshot": { "...MinerSnapshot..." }
  }
]
```

### GET /api/v1/miners/{id}
Один майнер по MAC/id.

### GET /api/v1/watchdogs
Список ватчдогов + последняя активность.

## Ingest (для ватчдогов)

### POST /api/v1/ingest/snapshots
Тело: `IngestBatchRequest { watchdogId, sentAt, snapshots[] }`.

```json
{ "accepted": 4, "rejected": 0, "serverMessage": null }
```

## Команды

### POST /api/v1/commands
Тело: `MinerCommand { requestId, minerId, kind, poolId? }`.

Kinds: `Reboot`, `StartMining`, `StopMining`, `RestartMining`, `PauseMining`, `ResumeMining`, `SwitchPool`.

```json
{
  "requestId": "abc123",
  "minerId": "02:A5:5D:B1:D4:C0",
  "success": true,
  "error": null,
  "completedAt": "..."
}
```

## Расписание

### GET /api/v1/schedules
### POST /api/v1/schedules
Body: `ScheduleCreateRequest { name, targetKind, targetId, enabled, timeZoneId, actions[] }`.

`targetKind`: `Miner` | `Watchdog` | `All`.
`actions[]`: `{ day, time, action }`. `day` — DayOfWeek (0-6), `time` — "HH:mm", `action` — `StartMining`/`StopMining`/`RestartMining`/`Reboot`.

### DELETE /api/v1/schedules/{id}
### POST /api/v1/schedules/{id}/enable
### POST /api/v1/schedules/{id}/disable
### GET /api/v1/schedules/{id}/runs
История срабатываний.

### GET /api/v1/schedules/timezones
Список доступных IANA-таймзон.

## Алерты

### GET /api/v1/alerts/rules
### POST /api/v1/alerts/rules
Body: `{ name, type, target, threshold, extra, forSeconds, cooldownSeconds, enabled, channel, channelConfig }`.

Types: `MinerOffline` | `HashrateBelow` | `TempAbove` | `StateIs`.

### DELETE /api/v1/alerts/rules/{id}
### POST /api/v1/alerts/rules/{id}/enable
### POST /api/v1/alerts/rules/{id}/disable
### GET /api/v1/alerts/events?limit=100
### POST /api/v1/alerts/test
Отправить тестовое сообщение в Telegram.

## Авто-действия

### GET /api/v1/autoactions
### POST /api/v1/autoactions
Body: `{ name, trigger, threshold, forSeconds, action, target, cooldownSeconds, enabled }`.

Triggers: `HashrateBelow` | `TempAbove` | `Offline`.
Actions: `Reboot` | `RestartMining` | `StopMining` | `PauseMining`.

### DELETE /api/v1/autoactions/{id}
### POST /api/v1/autoactions/{id}/enable
### POST /api/v1/autoactions/{id}/disable
### GET /api/v1/autoactions/events?limit=100

## Настройки

### GET /api/v1/settings
Возвращает `{ "Telegram:BotToken": "1234…abcd", ... }`. BotToken маскируется.

### PUT /api/v1/settings
Body: `{ "Telegram:BotToken": "...", "Telegram:DefaultChatId": "...", "Telegram:ProxyUrl": "http://127.0.0.1:8080" }`.

### POST /api/v1/settings/telegram/test

## SignalR

### /hubs/telemetry
Сервер → клиенты.
- Событие `miner.updated` — `{ minerId, state, hashrateGhs, hashratePercent, chipMaxTemp, powerW, timestamp }`.

### /hubs/control?watchdogId=<id>
Используется ватчдогами.
- Сервер → ватчдог: `ExecuteCommand`.
- Ватчдог → сервер: `ReportResult`.