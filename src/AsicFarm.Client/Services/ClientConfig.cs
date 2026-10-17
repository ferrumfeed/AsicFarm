using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsicFarm.Client.Services;

public sealed class ClientConfig
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AsicFarm");

    private static readonly string FilePath = Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ServerUrl { get; set; } = "http://localhost:5000";
    public string ApiKey { get; set; } = "";
    public bool IsDarkTheme { get; set; } = true;

    public ViewModels.MinerViewMode ViewMode { get; set; } = ViewModels.MinerViewMode.Cards;
    public ViewModels.MinerSortMode SortMode { get; set; } = ViewModels.MinerSortMode.ProblemsFirst;
    public bool ShowOnlyProblems { get; set; } = false;

    public static ClientConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new ClientConfig();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<ClientConfig>(json, JsonOpts) ?? new ClientConfig();
        }
        catch
        {
            return new ClientConfig();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(this, JsonOpts);
            File.WriteAllText(FilePath, json);
        }
        catch { }
    }
}