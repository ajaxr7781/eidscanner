using System.Collections.Concurrent;
using System.Net;
using System.Security;
using EidAgent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using EidAgent.Exceptions;
using EidAgent.Options;
using EidAgent.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "KeyVMS Emirates ID Agent";
});

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "eid-agent-.log");
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.File(
            path: logPath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14);

    if (OperatingSystem.IsWindows())
    {
        try
        {
            loggerConfiguration.WriteTo.EventLog(
                source: "KeyVMS.EidAgent",
                manageEventSource: false);
        }
        catch (SecurityException)
        {
            // Event Log may be inaccessible for some service accounts; continue with file logging.
        }
        catch (UnauthorizedAccessException)
        {
            // Event Log may be inaccessible for some service accounts; continue with file logging.
        }
    }
});

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.AddSingleton<IcaSdkClient, NativeIcaSdkClient>();
builder.Services.AddSingleton<IEidReader, IcaEidReader>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AgentCors", policy =>
    {
        var origins = builder.Configuration
            .GetSection("Agent:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        if (origins.Length > 0)
        {
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<RateLimiter>();

var port = builder.Configuration.GetValue<int?>("Agent:Port") ?? 9443;

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(port, listenOptions =>
    {
        listenOptions.UseHttps();
    });
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseCors("AgentCors");

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    ts = DateTimeOffset.UtcNow
}));

app.MapPost("/read-eid", async (HttpRequest request, IEidReader reader, IOptions<AgentOptions> options) =>
{
    var limiter = request.HttpContext.RequestServices.GetRequiredService<RateLimiter>();
    if (!limiter.TryAcquire(request.HttpContext.Connection.RemoteIpAddress))
    {
        return Results.Json(new { error = "rate_limited" }, statusCode: StatusCodes.Status429TooManyRequests);
    }

    var expectedSecret = options.Value.SharedSecret;
    if (string.IsNullOrWhiteSpace(expectedSecret) ||
        !request.Headers.TryGetValue("X-Shared-Secret", out var providedSecret) ||
        !string.Equals(providedSecret.ToString(), expectedSecret, StringComparison.Ordinal))
    {
        return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    try
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(request.HttpContext.RequestAborted);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        var result = await reader.ReadAsync(timeoutCts.Token);
        result.EidNumberMasked = MaskEidNumber(result.EidNumberMasked);
        return Results.Ok(result);
    }
    catch (EidAgentException ex)
    {
        var statusCode = ex.ErrorCode == EidAgentErrorCode.InternalError
            ? StatusCodes.Status500InternalServerError
            : StatusCodes.Status400BadRequest;
        return Results.Json(new { error = ex.ErrorCodeValue }, statusCode: statusCode);
    }
    catch (OperationCanceledException)
    {
        return Results.Json(new { error = "timeout" }, statusCode: StatusCodes.Status408RequestTimeout);
    }
    catch (Exception)
    {
        return Results.Json(new { error = "internal_error" }, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapGet("/config", (HttpRequest request, IOptions<AgentOptions> options) =>
{
    var expectedSecret = options.Value.SharedSecret;
    if (string.IsNullOrWhiteSpace(expectedSecret) ||
        !request.Headers.TryGetValue("X-Shared-Secret", out var providedSecret) ||
        !string.Equals(providedSecret.ToString(), expectedSecret, StringComparison.Ordinal))
    {
        return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(new
    {
        port = options.Value.Port,
        allowedOrigins = options.Value.AllowedOrigins
    });
});

app.Run();

static string MaskEidNumber(string rawValue)
{
    var digits = new string(rawValue.Where(char.IsDigit).ToArray());
    if (digits.Length <= 4)
    {
        return digits;
    }

    var lastFour = digits[^4..];
    var maskedPrefix = new string('*', digits.Length - 4);
    return $"{maskedPrefix}{lastFour}";
}

sealed class RateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _buckets = new();
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int Limit = 5;

    public bool TryAcquire(IPAddress? address)
    {
        var key = address?.ToString() ?? "unknown";
        var now = DateTimeOffset.UtcNow;
        var queue = _buckets.GetOrAdd(key, _ => new Queue<DateTimeOffset>());

        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > Window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= Limit)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}
