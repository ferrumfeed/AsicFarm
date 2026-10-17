# Архитектура AsicFarm Monitor

## Схема потоков данных

### Мониторинг (данные идут вверх)

```
Асик (VNISH REST) ──HTTP──► Watchdog ──HTTP батч──► Server ──SQLite──► API/SignalR
    каждые 30 сек          собрал снапшоты           принял батч       отдал клиенту
```

1. `SubnetScanner` в ватчдоге сканит подсеть (CIDR), находит VNISH-асики и добавляет в `MinerHostRegistry`.
2. `MinerPoller` раз в `PollInterval` опрашивает все хосты из реестра параллельно (`MaxConcurrency`).
3. Каждый `VnishAntminerClient.GetSnapshotAsync` дёргает `/info`, `/status`, `/summary` — параллельно.
4. Собранные `MinerSnapshot` шлются батчем на сервер через `POST /api/v1/ingest/snapshots`.
5. Сервер сохраняет последний снапшот каждого майнера (JSON-колонка) и пушит всем подписчикам SignalR.

### Управление (данные идут вниз)

```
UI ──HTTP──► Server ──SignalR──► Watchdog ──HTTP──► Асик
   команда      DispatchAsync    ExecuteAsync    VNISH endpoint
```

1. UI шлёт `POST /api/v1/commands` с `MinerCommand`.
2. Сервер ищет, какой ватчдог владеет этим майнером (`MinerEntity.WatchdogId`).
3. `ControlHub.DispatchAsync` шлёт `ExecuteCommand`-сообщение конкретному ватчдогу.
4. Ватчдог резолвит `minerId` (MAC) → IP через `MinerRegistry`, вызывает метод клиента.
5. Результат возвращается через `ReportResult` → завершает `TaskCompletionSource` на сервере → HTTP-ответ клиенту.

### Расписание (автономно на сервере)

```
ScheduleRunner (BackgroundService, tick 20 сек)
  ├─ читает Schedules из БД
  ├─ считает локальное время в TZ расписания
  ├─ для каждого DayAction проверяет: попало ли в окно (lastTick, now]
  └─ если да — DispatchAsync (как UI-команда)
```

### Алерты (автономно на сервере)

```
AlertRunner (BackgroundService, tick 30 сек)
  ├─ читает AlertRules
  ├─ для каждой цели (All | Miner | Watchdog) проверяет условие
  ├─ если условие держится ForSeconds и Cooldown прошёл — FireAsync
  └─ отправляет через TelegramNotifier
```

### Авто-действия

```
AutoActionRunner (BackgroundService, tick 30 сек)
  ├─ проверяет условия (HashrateBelow / TempAbove / Offline)
  ├─ Global rate-limit: не больше 5 действий/минуту на всю ферму
  ├─ Per-miner cooldown из правила
  └─ DispatchAsync к ватчдогу
```

---

## Проекты и их ответственность

### AsicFarm.Shared

Никакой логики — только модели, контракты, DTO.

```
Domain/          домен (MinerSnapshot, MinerState, ChainSnapshot, PoolSnapshot...)
Vnish/           DTO для ответов VNISH + VnishJson (опции System.Text.Json)
Contracts/       IAntminerClient, MinerCommand, ScheduleDto, AlertRuleDto...
Exceptions/      MinerUnreachableException, MinerAuthException, MinerApiException
Json/            WireJson.Options — общие опции сериализации (camelCase, Web)
```

**Почему разделяем Domain и Vnish:** завтра добавим Stock-клиент. Меняться будет только `MinerClients`, а `Domain` останется как есть.

### AsicFarm.MinerClients

Реализации `IAntminerClient`.

```
Vnish/
  VnishAntminerClient.cs        HTTP-клиент, авторизация, ретраи
  VnishAntminerClientOptions.cs конфиг клиента
  VnishAuthState.cs             JWT-токен + ленивая переполучка
  VnishEndpoints.cs             строковые константы путей
  VnishMapping.cs               VnishXxx → Domain
Stock/                          (пусто, в планах)
```

**Особенности VNISH:**
- Публичные эндпоинты (без пароля): `/info`, `/status`, `/summary`, `/metrics`, `/chains`, `/model`
- Требуют авторизации: `/mining/*`, `/system/reboot`, `/settings`
- Два режима: JWT (`POST /unlock {pw}` → token) и API-ключ (`x-api-key` header)

### AsicFarm.Watchdog

Ферменный воркер. Один инстанс на физическую локацию.

```
WatchdogOptions.cs              конфиг из appsettings.json
ServerClient.cs                 HTTP-отправка батчей на сервер
MinerPoller.cs                  опрос асиков по расписанию
Services/
  MinerHostRegistry.cs          потокобезопасный реестр хостов
  MinerRegistry.cs              MAC ↔ IP (для обратного канала)
  SubnetScanner.cs              автопоиск в CIDR
  ControlChannel.cs             SignalR-приём команд от сервера
Program.cs                      DI + HostedServices
```

**HostedServices запускаются параллельно:**
- `SubnetScanner` — скан при старте + каждые `RescanIntervalMinutes`
- `MinerPoller` — тик каждые `PollInterval`
- `ControlChannel` — постоянное SignalR-соединение

### AsicFarm.Server

ASP.NET Core Minimal API + SignalR + EF Core.

```
Auth/
  ServerOptions.cs              {ApiKey, DatabasePath}
  ApiKeyMiddleware.cs           проверка X-Api-Key
Data/
  Entities.cs                   EF-сущности
  AsicFarmDbContext.cs          контекст + модели
Endpoints/
  MinerEndpoints.cs             GET /miners, /health
  IngestEndpoints.cs            POST /ingest/snapshots
  CommandEndpoints.cs           POST /commands
  ScheduleEndpoints.cs          CRUD расписаний
  AlertEndpoints.cs             CRUD алертов
  AutoActionEndpoints.cs        CRUD авто-действий
  SettingsEndpoints.cs          GET/PUT /settings (Telegram)
Hubs/
  TelemetryHub.cs               realtime в UI (событие miner.updated)
  ControlHub.cs                 обратный канал ватчдогам + pending results
Services/
  ScheduleRunner.cs             BackgroundService — исполнение расписаний
  AlertRunner.cs                BackgroundService — алерты
  AutoActionRunner.cs           BackgroundService — авто-действия
  TelegramNotifier.cs           отправка в Telegram (с поддержкой прокси)
  SettingsService.cs            override настроек из БД поверх appsettings
  TzUtil.cs                     IANA ↔ Windows timezone resolver
Program.cs
```

### AsicFarm.Client

WPF + MVVM (CommunityToolkit).

```
Api/ServerApiClient.cs          HTTP-обёртка над сервером (использует X-Api-Key)
Services/
  TelemetryHubClient.cs         SignalR-подписка на miner.updated
  BitcoinNetworkService.cs      курс BTC + difficulty (3 fallback-источника)
ViewModels/
  MainViewModel.cs              корневая VM, метрики фермы, список майнеров
  MinerItemViewModel.cs         карточка майнера + команды
  ScheduleViewModel.cs          вкладка "Расписание"
App.xaml.cs                     DI-контейнер + тема
MainWindow.xaml.cs              bootstrap MainViewModel + ScheduleViewModel
```

---

## Модель данных (SQLite)

### Miners
```
Id (PK, string)       MAC или серийник
Ip, Mac, Hostname     из /info
Model, ModelCode      "Antminer S19k Pro", "s19kpro"
Algorithm             "sha256d"
FirmwareName/Version  "VNISH", "1.2.7"
WatchdogId            какой ватчдог "владеет" майнером
FirstSeen, LastSeen
LastSnapshotJson      полный MinerSnapshot (camelCase JSON)
```

### Schedules
```
Id (PK, long)
Name
TargetKind            "Miner" | "Watchdog" | "All"
TargetId
Enabled
TimeZoneId            "Asia/Yekaterinburg" (IANA)
ActionsJson           JSON: [ {day, time, action}, ... ]
CreatedAt, LastRunAt
```

### ScheduleRuns
История срабатываний.

### AlertRules / AlertEvents
Правило: `Type`, `Target`, `Threshold`, `ForSeconds`, `CooldownSeconds`, `Channel`, `ChannelConfig`.
Событие: `RuleId`, `MinerId`, `FiredAt`, `Message`, `Delivered`, `DeliveryError`.

### AutoActions / AutoActionEvents
Правило: `Trigger`, `Threshold`, `ForSeconds`, `Action`, `Target`, `CooldownSeconds`.
Событие: `RuleId`, `MinerId`, `Action`, `FiredAt`, `Success`, `Error`.

### AppSettings
Key-Value override над appsettings.json. Используется для `Telegram:BotToken`, `Telegram:DefaultChatId`, `Telegram:ProxyUrl`.

### Watchdogs
Реестр: `Id`, `Name`, `FirstSeen`, `LastSeen`, `TotalSnapshotsReceived`.

---

## Ключевые модели домена

### MinerSnapshot

Всегда в GH/s. Поля хешрейта:
- `HashrateRealtimeGhs` — мгновенный
- `HashrateAverageGhs` — средний за сессию
- `HashrateNominalGhs` — номинальный (для расчёта %)
- `HashrateStockGhs` — стоковый
- `HashratePercent` — realtime / nominal × 100

Температуры — `TempRange(Min, Max)` отдельно для `ChipTemp`, `PcbTemp`, `PsuTemp*`.

Питание — `PowerConsumptionW`, `PowerEfficiencyJPerTh`.

### IAntminerClient

```csharp
Task<FirmwareProbeResult> ProbeAsync(ct);
Task<MinerIdentity>       GetIdentityAsync(ct);
Task<MinerSnapshot>       GetSnapshotAsync(ct);
Task<MinerSnapshot>       GetDetailedSnapshotAsync(ct);
Task<MetricsHistory>      GetMetricsAsync(window, step, ct);

Task RebootAsync(ct);
Task StartMiningAsync(ct);
Task StopMiningAsync(ct);
Task RestartMiningAsync(ct);
Task PauseMiningAsync(ct);
Task ResumeMiningAsync(ct);
Task SwitchPoolAsync(poolId, ct);
```

---

## Аутентификация

### Клиент ↔ Сервер
`X-Api-Key` header. Ключ в `appsettings.json` → `Server:ApiKey`.

### Ватчдог ↔ Сервер
Тот же `X-Api-Key`. Ватчдог прописывает его в `appsettings.json` → `Watchdog:ServerApiKey`.

Для SignalR ключ передаётся в header при подключении.

### Ватчдог ↔ Асик
Пароль от веб-интерфейса VNISH. Если задан — клиент лениво получает JWT через `POST /unlock {pw}`.

**Один общий пароль на все асики** — на первом этапе. Per-host override в планах.

---

## Решения, которые нужно знать

### InvariantGlobalization

В `Directory.Build.props` стоит `InvariantGlobalization=true`. Это **ломает `TimeZoneInfo`** — работает только UTC.

Поэтому у трёх проектов стоит override `InvariantGlobalization=false`:
- `AsicFarm.Server` — расписания в TZ
- `AsicFarm.Watchdog`
- `AsicFarm.Client` — WPF требует ICU

### IANA ↔ Windows timezones

`TimeZoneInfo.FindSystemTimeZoneById("Asia/Yekaterinburg")` на Windows **падает**. Решается через `TzUtil.TryResolve`.

### HttpClient в DI

`AddHttpClient<T>()` регистрирует T как transient. Это ломает `ServerApiClient` — `MainViewModel` и `ScheduleViewModel` получают разные экземпляры. Поэтому используем `AddSingleton<HttpClient>()` + `AddSingleton<ServerApiClient>()`.

### EnsureDeleted в Development

`Program.cs` сервера при старте в Development **удаляет и пересоздаёт БД**. Удобно при смене схемы.

### Rate-limits для авто-действий

`AutoActionRunner` имеет **глобальный rate-limit 5 действий/минуту**.

### Единицы хешрейта

VNISH отдаёт в GH/s (sha256d) или MH/s (scrypt). Домен всегда в GH/s.

**Грабли:** пороги `switch` должны учитывать, что значение уже в GH/s. Например, `1e3` = 1 TH/s.

---

## Известные проблемы

1. **T21 в AutoTuning** — иногда долго висит при перетюне. Нормально.
2. **Отрицательные температуры** — VNISH иногда возвращает мусор во время автотюна. В UI отфильтровываем `Math.Max(0, value)`.
3. **Telegram в РФ** — через `Telegram:ProxyUrl` (HTTP-прокси) работает.
4. **200+ майнеров** — при интервале < 10 сек нужен stagger. Пока не реализован.