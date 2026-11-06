using System.Text.Json;

namespace AsicFarm.Server.Endpoints;

public static class UpdateEndpoints
{
    private static readonly string UpdateDir = Path.Combine(AppContext.BaseDirectory, "updates");

    public static void MapUpdates(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/updates/latest", GetLatestUpdate);
        app.MapGet("/updates/{fileName}", GetUpdateFile);
    }

    private static IResult GetLatestUpdate()
    {
        var jsonPath = Path.Combine(UpdateDir, "latest.json");
        if (!File.Exists(jsonPath))
            return Results.NotFound(new { error = "update metadata not found", lookedAt = jsonPath });

        var json = File.ReadAllText(jsonPath);
        return Results.Content(json, "application/json");
    }

    private static IResult GetUpdateFile(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        var filePath = Path.Combine(UpdateDir, safeName);

        if (!File.Exists(filePath))
            return Results.NotFound(new { error = "file not found", lookedAt = filePath });

        // Если это текстовый файл (чейнджлог), отдаем его как обычный текст без принудительного скачивания
        if (Path.GetExtension(filePath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var text = File.ReadAllText(filePath);
            return Results.Text(text, "text/plain; charset=utf-8");
        }

        // Для всех остальных файлов (ZIP) отдаем как скачиваемый файл
        var fileInfo = new FileInfo(filePath);
        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Results.File(
            stream,
            "application/octet-stream",
            safeName,
            enableRangeProcessing: true
        );
    }
}