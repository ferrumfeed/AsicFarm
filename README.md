# AsicFarm Monitor

Система мониторинга и управления фермами ASIC-майнеров (Antminer на прошивке VNISH).
Собирает метрики со всех майнеров в подсети, отображает в реальном времени, даёт управлять
(ребут / старт / стоп / рестарт), строит расписание включения-выключения, шлёт алерты в Telegram,
делает авто-действия при проблемах.

## Возможности (текущий статус)

- [x] Мониторинг VNISH-майнеров (S19/T21/L7 и др.) через REST API
- [x] Автопоиск асиков в подсети (CIDR scan)
- [x] Real-time UI через SignalR
- [x] Команды: Start / Stop / Restart Mining, Reboot
- [x] Расписание вкл/выкл по дням недели и времени (несколько действий в день)
- [x] Общие метрики фермы: суммарный хешрейт, потребление
- [x] Курс BTC + сложность сети + оценка доходности (gross)
- [x] Алерты в Telegram (offline, просадка хешрейта, перегрев)
- [x] Авто-действия при проблемах (ребут/рестарт/пауза)
- [ ] UI-вкладка для Настроек и Алертов (пока API через .http)
- [ ] История графиков (хешрейт/температура за неделю)
- [ ] Сток-прошивка Antminer (не-VNISH)
- [ ] Self-contained single-file publish для ватчдога
- [ ] Windows Service / systemd обёртка

## Стек

| Компонент | Технология |
|---|---|
| Runtime | .NET 8.0 |
| Сервер | ASP.NET Core Minimal API + SignalR + EF Core + SQLite |
| Ватчдог | .NET Worker Service + SignalR.Client |
| Клиент | WPF + WPF-UI (Fluent) + CommunityToolkit.Mvvm |
| JSON | System.Text.Json |

## Архитектура (кратко)

```
   Antminer (VNISH) ─────────┐
   Antminer (VNISH) ─────────┤  HTTP /api/v1/*
   Antminer (VNISH) ─────────┤
   ... 200+ ...              │
                             ▼
                    ┌──────────────────┐
                    │  AsicFarm.Watchdog  (на ферме, exe)
                    │  - сканит подсеть   │
                    │  - опрашивает асики │
                    │  - шлёт батчи       │
                    │  - принимает команды│
                    └─────────┬─────────┘
                              │  SignalR + HTTP
                              ▼
                    ┌──────────────────┐
                    │  AsicFarm.Server    (VPS / локально)
                    │  - принимает снапшоты │
                    │  - хранит в SQLite    │
                    │  - отдаёт API         │
                    │  - расписание, алерты │
                    └─────────┬─────────┘
                              │  SignalR + REST
                              ▼
                    ┌──────────────────┐
                    │  AsicFarm.Client    (WPF UI)
                    │  - live-обновления │
                    │  - управление      │
                    │  - расписание      │
                    └──────────────────┘
```

Подробнее — см. `ARCHITECTURE.md`.

## Быстрый старт

### Требования
- .NET 8.0 SDK
- Windows для клиента (WPF)
- Любая ОС для сервера и ватчдога

### Сборка
```bash
dotnet restore
dotnet build
```

### Запуск (3 окна)

**1. Сервер**
```bash
dotnet run --project src/AsicFarm.Server
```
Слушает на `http://0.0.0.0:5000`.

**2. Ватчдог**
```bash
dotnet run --project src/AsicFarm.Watchdog
```
Настраивается через `src/AsicFarm.Watchdog/appsettings.json`.

**3. Клиент**
```bash
dotnet run --project src/AsicFarm.Client
```
При запуске — введи URL сервера и API-ключ из `src/AsicFarm.Server/appsettings.json` → `Server:ApiKey`.

### Разовый пробник (без сервера)
```bash
dotnet run --project samples/AsicFarm.Probe -- 192.168.2.101 --metrics
```

## Структура решения

```
AsicFarm.sln
├── Directory.Build.props         общие MSBuild-настройки
├── Directory.Packages.props      централизованные версии NuGet
├── README.md                     этот файл
├── ARCHITECTURE.md               техническая документация
├── API.md                        список всех эндпоинтов сервера
├── DEVELOPMENT.md                конвенции, грабли, как продолжать
├── src/
│   ├── AsicFarm.Shared/          DTO, контракты, доменные модели
│   ├── AsicFarm.MinerClients/    клиенты асиков (Vnish, позже Stock)
│   ├── AsicFarm.Watchdog/        ферменный воркер (exe)
│   ├── AsicFarm.Server/          веб-API + SignalR (exe)
│   └── AsicFarm.Client/          WPF UI (exe)
└── samples/
    └── AsicFarm.Probe/           консольный пробник (exe)
```

## Ключевые файлы (для быстрого погружения)

- `src/AsicFarm.Shared/Domain/MinerSnapshot.cs` — главная модель снапшота
- `src/AsicFarm.Shared/Contracts/IAntminerClient.cs` — контракт клиента майнера
- `src/AsicFarm.MinerClients/Vnish/VnishAntminerClient.cs` — реализация под VNISH
- `src/AsicFarm.Watchdog/Services/SubnetScanner.cs` — автопоиск асиков
- `src/AsicFarm.Server/Hubs/ControlHub.cs` — обратный канал (UI → ватчдог → асик)
- `src/AsicFarm.Server/Services/ScheduleRunner.cs` — исполнение расписаний
- `src/AsicFarm.Client/ViewModels/MainViewModel.cs` — корневая VM клиента