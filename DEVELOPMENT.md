# Разработка

## Как продолжать в новом диалоге с ИИ

1. Открой `README.md` + `ARCHITECTURE.md` + этот файл.
2. Скинь их в чат + пару ключевых файлов, которые трогаем сейчас.
3. Скажи: «Продолжаем проект AsicFarm. Мы на этапе X. Хочу делать Y».

Этого достаточно — контекст восстановится за 30 секунд.

## Конвенции

### Неймспейсы
- `AsicFarm.Shared.Domain` — домен
- `AsicFarm.Shared.Vnish` — DTO VNISH
- `AsicFarm.Shared.Contracts` — интерфейсы и DTO для API
- `AsicFarm.MinerClients.Vnish` — клиент VNISH
- `AsicFarm.Watchdog.Services` — фоновые сервисы ватчдога
- `AsicFarm.Server.{Auth,Data,Endpoints,Hubs,Services}` — серверные слои
- `AsicFarm.Client.{Api,Services,ViewModels}` — клиент

### Модели
- DTO для JSON — `record` (immutable).
- Domain-модели — `record`.
- EF-сущности — `class` с `{ get; set; }` (EF требует mutable).
- MVVM — `partial class : ObservableObject`, поля через `[ObservableProperty]`, команды `[RelayCommand]`.

### JSON
- Между проектами — `JsonSerializerDefaults.Web` (camelCase).
- VNISH — `VnishJson.Options` (snake_case).

### Обработка ошибок в клиентах
`MinerUnreachableException` / `MinerAuthException` / `MinerApiException`. Ватчдог их ловит и логирует.

### Rate-limits
Любые фоновые операции должны иметь cooldown. См. `AutoActionRunner.MaxActionsPerMinute`.

### Уведомления WPF
**Вычисляемые свойства** (`public string XxxDisplay => ...`) не обновляются сами. Нужен явный `OnPropertyChanged(nameof(XxxDisplay))`.

## Грабли, на которые мы наступали

| Симптом | Причина | Решение |
|---|---|---|
| `CS8805: Программы, использующие инструкции верхнего уровня, должны быть исполняемыми` | У console-проекта нет `<OutputType>Exe</OutputType>` | Добавить в csproj |
| `CS8022: Feature 'file-scoped namespace' is not available` | Нет `<LangVersion>12</LangVersion>` | В `Directory.Build.props` |
| `Could not parse the JSON file. 'a' is an invalid start of a value` | Битый `appsettings.json` | Пересоздать, проверить кавычки |
| `no such table: Schedules` | Схема изменилась | Удалить `asicfarm.db` или `EnsureDeleted` в Development |
| `Unable to resolve service for type 'ServerClient'` | Нет `AddHttpClient<ServerClient>()` | Добавить регистрацию |
| `"TimeSpan" не содержит определения "Value"` | `delta` не nullable | Использовать `delta` напрямую |
| `Cannot find non-neutral culture related to 'en-us'` (WPF) | `InvariantGlobalization=true` | Override в csproj |
| `unknown timezone 'Asia/Yekaterinburg'` | Windows не знает IANA | `TimeZoneInfo.TryConvertIanaIdToWindowsId` |
| Метрики показывают 0 при появлении данных | Вычисляемые свойства не шлют `PropertyChanged` | Явно вызвать `OnPropertyChanged` |
| `This instance has already started one or more requests` | `DefaultRequestHeaders` менять после старта нельзя | Заголовок с каждым `HttpRequestMessage` |
| Расписание: dropdown пустой | Разные `ServerApiClient` | `AddSingleton` |
| Hashrate: 777.90 GH/s вместо TH/s | Пороги switch для H/s, а значение в GH/s | Делители `1e3/1e6/1e9` 
|| TextBox не принимает ввод (ни клавиатура, ни Ctrl+V) | `ui:FluentWindow` + `ui:TitleBar` из WPF-UI 4.3.0 перехватывают клавиатуру | Использовать обычный `Window` + `ui:Button`/`ui:Card`/`ui:TextBox` внутри (стили сохраняются) |
| TextBox не принимает ввод (ни клавиатура, ни Ctrl+V) | `ui:FluentWindow` + `ui:TitleBar` из WPF-UI 4.3.0 перехватывают клавиатуру | Использовать обычный `Window` + `ui:Button`/`ui:Card`/`ui:TextBox` внутри — стили сохраняются |

## Тестирование руками

### Пробник VNISH-клиента
```bash
dotnet run --project samples/AsicFarm.Probe -- 192.168.2.101 --metrics --log info
```

### Тестовый `.http`
Файл `src/AsicFarm.Server/requests.http`. В VS — кнопка **Send Request**.

## Публикация (в планах)
### Итог по UI (важно знать при возврате к проекту)
Главное окно — обычный `Window`, не `FluentWindow`. Причина: `ExtendsContentIntoTitleBar` + Mica от WPF-UI 4.3.0 ломают ввод текста во всех TextBox.

### Self-contained single-file
```bash
dotnet publish src/AsicFarm.Watchdog -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true
```

## Что дальше (roadmap)

1. UI-вкладки **Настройки** и **Алерты**
2. История метрик (графики)
3. Self-contained publish + Windows Service для ватчдога
4. Сток-прошивка Antminer
5. Staggered polling для 200+ майнеров
6. JWT-аутентификация UI
7. EF Core миграции
8. Multiple watchdogs UI

## Как добавить новую фичу

**Серверная часть:**
1. Добавить сущность в `Entities.cs`
2. Зарегистрировать `DbSet` в `AsicFarmDbContext`
3. Создать `Endpoints/XxxEndpoints.cs`
4. Прописать `app.MapXxx()` в `Program.cs`
5. Если нужна фоновая логика — `Services/XxxRunner.cs : BackgroundService` + регистрация

**Клиентская часть:**
1. Метод в `ServerApiClient`
2. DTO в `AsicFarm.Shared.Contracts`
3. ViewModel (наследник `ObservableObject`, `partial`)
4. XAML — разметка
5. Зарегистрировать VM в `App.xaml.cs`