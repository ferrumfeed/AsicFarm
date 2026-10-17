using Microsoft.AspNetCore.SignalR;

namespace AsicFarm.Server.Hubs;

public sealed class TelemetryHub : Hub
{
    public const string Route = "/hubs/telemetry";

    // Клиент может подписаться на группу конкретного ватчдога или фермы.
    // Пока — просто логируем, группы введём при появлении UI.
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}