using Microsoft.Extensions.Options;

namespace AsicFarm.Server.Auth;

public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext ctx, IOptions<ServerOptions> opts)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        if (!ctx.Request.Headers.TryGetValue(HeaderName, out var provided))
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "Missing X-Api-Key header" });
            return;
        }

        var expected = opts.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(expected) || provided.ToString() != expected)
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
            return;
        }

        await next(ctx);
    }
}