using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Http;
using Serilog;
using EidAgent.Options;
using EidAgent.Services;
using EidAgent.Exceptions;
using EidAgent.Models;

var builder = WebApplication.CreateBuilder(args);

// Run as Windows service
builder.Host.UseWindowsService(o => o.ServiceName = "KeyVMS Emirates ID Agent");

// Bind options
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
var opts = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new AgentOptions();

// Serilog (file under app base dir)
var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.File(Path.Combine(logDir, "eid-agent-.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// CORS
builder.Services.AddCors(p =>
{
    p.AddPolicy("cors", policy =>
    {
        if (opts.AllowedOrigins is { Length: > 0 })
            policy.WithOrigins(opts.AllowedOrigins).AllowAnyHeader().AllowAnyMethod();
        else
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// Reader DI (swap later to IcaEidReader)
builder.Services.AddSingleton<IEidReader, FakeEidReader>();

var app = builder.Build();

app.UseCors("cors");

// Listen on HTTPS localhost only
app.Urls.Clear();
app.Urls.Add($"https://127.0.0.1:{opts.Port}");

// --- Simple in-memory rate limiter: 5 per minute per client IP
var limiter = new SlidingWindowRateLimiter(maxPerWindow: 5, window: TimeSpan.FromMinutes(1));

bool IsAuthorized(HttpRequest req)
{
    if (!req.Headers.TryGetValue("X-Shared-Secret", out var s)) return false;
    return !string.IsNullOrWhiteSpace(opts.SharedSecret) &&
           string.Equals(s.ToString(), opts.SharedSecret, StringComparison.Ordinal);
}

string ClientKey(HttpContext ctx)
{
    // Usually 127.0.0.1 for localhost calls; OK for this agent
    return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

app.MapGet("/health", () => Results.Ok(new { status = "ok", ts = DateTimeOffset.UtcNow }));

app.MapGet("/config", (HttpContext ctx) =>
{
    if (!IsAuthorized(ctx.Request))
        return Results.Unauthorized();

    return Results.Ok(new
    {
        port = opts.Port,
        allowedOrigins = opts.AllowedOrigins ?? Array.Empty<string>()
    });
});

app.MapPost("/read-eid", async (HttpContext ctx, IEidReader reader) =>
{
    if (!IsAuthorized(ctx.Request))
        return Results.Unauthorized();

    var key = ClientKey(ctx);
    if (!limiter.Allow(key))
        return Results.StatusCode((int)HttpStatusCode.TooManyRequests);

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
    cts.CancelAfter(TimeSpan.FromSeconds(15));

    try
    {
        var resp = await reader.ReadAsync(cts.Token);

        // Ensure masking rule: digits only, show last 4
        resp = resp with { EidNumberMasked = MaskDigitsLast4(resp.EidNumberMasked) };

        return Results.Ok(resp);
    }
    catch (OperationCanceledException)
    {
        return Results.BadRequest(new { error = "timeout" });
    }
    catch (EidAgentException ex)
    {
        return Results.BadRequest(new { error = ex.Code, message = ex.Message });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Unhandled /read-eid error");
        return Results.StatusCode(500);
    }
});

app.Run();

static string MaskDigitsLast4(string input)
{
    var digits = new string((input ?? "").Where(char.IsDigit).ToArray());
    if (digits.Length <= 4) return "****" + digits;
    return "****" + digits[^4..];
}

// ---------------- Rate limiter ----------------
sealed class SlidingWindowRateLimiter
{
    private readonly int _max;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _hits = new();

    public SlidingWindowRateLimiter(int maxPerWindow, TimeSpan window)
    {
        _max = maxPerWindow;
        _window = window;
    }

    public bool Allow(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var q = _hits.GetOrAdd(key, _ => new ConcurrentQueue<DateTimeOffset>());
        q.Enqueue(now);

        while (q.TryPeek(out var ts) && now - ts > _window)
            q.TryDequeue(out _);

        return q.Count <= _max;
    }
}
