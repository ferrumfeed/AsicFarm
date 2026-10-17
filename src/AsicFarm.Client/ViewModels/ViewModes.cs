namespace AsicFarm.Client.ViewModels;

public enum MinerViewMode
{
    Cards,
    Compact,
}

public enum MinerSortMode
{
    ProblemsFirst,   // Проблемные сверху
    NameAsc,         // По имени
    HashrateDesc,    // Хешрейт: высокий → низкий
    HashrateAsc,     // Хешрейт: низкий → высокий
    TempDesc,        // Температура: горячие сверху
    EfficiencyDesc,  // Энергоэффективность: неэффективные сверху
}