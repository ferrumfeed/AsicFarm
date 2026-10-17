using System.Text.Json;

namespace AsicFarm.Shared.Json;

public static class WireJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}