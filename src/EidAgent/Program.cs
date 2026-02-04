using EidAgent.Exceptions;
using EidAgent.Options;
using EidAgent.Services;
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
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.File(
            path: "logs\\eid-agent-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14)
        .WriteTo.EventLog(
            source: "KeyVMS.EidAgent",
            manageEventSource: true);
});

builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.AddSingleton<IEidReader, FakeEidReader>();

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
    var expectedSecret = options.Value.SharedSecret;
    if (string.IsNullOrWhiteSpace(expectedSecret) ||
        !request.Headers.TryGetValue("X-Shared-Secret", out var providedSecret) ||
        !string.Equals(providedSecret.ToString(), expectedSecret, StringComparison.Ordinal))
    {
        return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    try
    {
        var result = await reader.ReadAsync(request.HttpContext.RequestAborted);
        return Results.Ok(result);
    }
    catch (EidAgentException ex)
    {
        var statusCode = ex.ErrorCode == EidAgentErrorCode.InternalError
            ? StatusCodes.Status500InternalServerError
            : StatusCodes.Status400BadRequest;
        return Results.Json(new { error = ex.ErrorCodeValue }, statusCode);
    }
    catch (Exception)
    {
        return Results.Json(new { error = "internal_error" }, StatusCodes.Status500InternalServerError);
    }
});

app.Run();
