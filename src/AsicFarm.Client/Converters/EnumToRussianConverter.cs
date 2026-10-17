using System.Globalization;
using System.Windows.Data;

namespace AsicFarm.Client.Converters;

public sealed class EnumToRussianConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Map = new()
    {
        // ===== MinerState =====
        ["Mining"] = "Майнит",
        ["Initializing"] = "Инициализация",
        ["Starting"] = "Запуск",
        ["AutoTuning"] = "Автотюнинг",
        ["Restarting"] = "Перезапуск",
        ["ShuttingDown"] = "Остановка",
        ["Stopped"] = "Остановлен",
        ["Failure"] = "Ошибка",
        ["Unknown"] = "—",

        // ===== AlertRuleType =====
        ["MinerOffline"] = "Майнер не отвечает",
        ["HashrateBelow"] = "Хешрейт упал ниже",
        ["TempAbove"] = "Температура выше",
        ["StateIs"] = "Смена состояния",

        // ===== AlertTargetKind / ScheduleTargetKind =====
        ["All"] = "Все майнеры",
        ["Miner"] = "Один майнер",
        ["Watchdog"] = "Все мои майнеры",

        // ===== AutoActionTrigger =====
        ["Offline"] = "Нет связи",

        // ===== AutoActionKind / MinerCommandKind / ScheduleAction =====
        ["Reboot"] = "Перезагрузка",
        ["RestartMining"] = "Перезапуск майнинга",
        ["StartMining"] = "Запуск майнинга",
        ["StopMining"] = "Остановка майнинга",
        ["PauseMining"] = "Пауза",
        ["ResumeMining"] = "Продолжить",
        ["SwitchPool"] = "Смена пула",
        ["None"] = "Не выполнять",

        // ===== PoolStatus =====
        ["Working"] = "Работает",
        ["Active"] = "Активен",
        ["Rejecting"] = "Отклоняет",
        ["Disabled"] = "Отключён",

        // ===== ChainState =====
        ["Disconnected"] = "Отключена",

        // ===== FanState =====
        ["Ok"] = "ОК",
        ["Lost"] = "Потерян",

        // ===== FanControlMode =====
        ["Auto"] = "Авто",
        ["Manual"] = "Ручной",
        ["Immersion"] = "Иммерсия",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return "";
        var key = value.ToString() ?? "";
        return Map.TryGetValue(key, out var ru) ? ru : key;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}