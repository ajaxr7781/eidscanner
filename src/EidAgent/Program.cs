using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Http;
using Serilog;
using EidAgent.Options;
using EidAgent.Services;
using EidAgent.Exceptions;
using EidAgent.Models;
using AE.EmiratesId.IdCard;
using AE.EmiratesId.IdCard.DataModels;

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
    return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

// ---------- SDK / Native load helpers ----------
var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
var pluginsDir = Path.Combine(exeDir, "plugins");

// Optional: vendor SDK root (only used as fallback search path)
var sdkRoot = @"C:\ProgramData\KeyVMS\id-card-toolkit-windows-dotnet-sdk-v3.0.2-r3\";
var nativeDllName = "EIDAToolkit.dll";
var nativeDllPath = Path.Combine(exeDir, nativeDllName);

// Configure DLL search paths early (best effort)
try
{
    NativeDllSearch.AddSearchPath(exeDir);
    if (Directory.Exists(pluginsDir)) NativeDllSearch.AddSearchPath(pluginsDir);

    if (Directory.Exists(sdkRoot))
    {
        NativeDllSearch.AddSearchPath(sdkRoot);
        var sdkPlugins = Path.Combine(sdkRoot, "plugins");
        if (Directory.Exists(sdkPlugins)) NativeDllSearch.AddSearchPath(sdkPlugins);
    }
}
catch (Exception ex)
{
    Log.Warning(ex, "Failed configuring native DLL search paths");
}
// ---------------------------------------------

app.MapGet("/health", () => Results.Ok(new { status = "ok", ts = DateTimeOffset.UtcNow.ToString("o") }));

// SDK check endpoint (protected) - proves:
// 1) plugins folder presence
// 2) native EIDAToolkit.dll load
// 3) managed Toolkit call works (GetDataProtectionKey)
app.MapGet("/sdk-check", (HttpContext ctx) =>
{
    if (!IsAuthorized(ctx.Request))
        return Results.Unauthorized();

    var report = new Dictionary<string, object?>();
    var errors = new List<string>();

    report["ts"] = DateTimeOffset.UtcNow.ToString("o");
    report["processPath"] = Environment.ProcessPath;
    report["baseDir"] = AppContext.BaseDirectory;
    report["exeDir"] = exeDir;
    report["os"] = RuntimeInformation.OSDescription;
    report["arch"] = RuntimeInformation.ProcessArchitecture.ToString();

    report["pluginsDir"] = pluginsDir;
    report["pluginsDirExists"] = Directory.Exists(pluginsDir);

    try
    {
        if (Directory.Exists(pluginsDir))
            report["pluginsCount"] = Directory.EnumerateFiles(pluginsDir, "*", SearchOption.AllDirectories).Count();
    }
    catch (Exception ex)
    {
        errors.Add("Unable to enumerate plugins directory: " + ex.Message);
    }

    report["nativeDllName"] = nativeDllName;
    report["nativeDllPath"] = nativeDllPath;
    report["nativeDllExistsNextToExe"] = File.Exists(nativeDllPath);

    // Native load test
    try
    {
        IntPtr handle;
        if (File.Exists(nativeDllPath))
        {
            handle = NativeLibrary.Load(nativeDllPath);
            report["nativeLoad"] = "Loaded by absolute path (next to EXE)";
        }
        else
        {
            handle = NativeLibrary.Load(nativeDllName);
            report["nativeLoad"] = "Loaded by name via DLL search paths";
        }

        report["nativeHandleNonZero"] = handle != IntPtr.Zero;
    }
    catch (Exception ex)
    {
        errors.Add("NativeLibrary.Load failed for EIDAToolkit.dll: " + ex.Message);
        report["nativeLoad"] = "FAILED";
        report["nativeHandleNonZero"] = false;
    }

 // Managed toolkit call test (real call, correct constructor)
try
{
    var inProcessMode = true; // local agent: run in-process
    var pluginPathToUse = Directory.Exists(pluginsDir) ? pluginsDir : Path.Combine(sdkRoot, "plugins");

    report["toolkitInProcessMode"] = inProcessMode;
    report["toolkitPluginsPath"] = pluginPathToUse;
    report["toolkitPluginsPathExists"] = Directory.Exists(pluginPathToUse);

    var toolkit = new Toolkit(inProcessMode, pluginPathToUse);
    DataProtectionKey key = toolkit.GetDataProtectionKey();

    report["managedToolkit"] = "AE.EmiratesId.IdCard.Toolkit";
    report["managedCall"] = "GetDataProtectionKey() OK";
    report["publicKeyLen"] = key?.PublicKey?.Length ?? 0;
}
catch (Exception ex)
{
    errors.Add("Managed Toolkit.GetDataProtectionKey() failed: " + ex.Message);
    report["managedCall"] = "FAILED";
}


    report["errors"] = errors;
    report["ok"] = errors.Count == 0;

    return Results.Json(report);
});

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

// ---------------- Native DLL search helper ----------------
static class NativeDllSearch
{
    private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
    private const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr AddDllDirectory([MarshalAs(UnmanagedType.LPWStr)] string newDirectory);

    private static bool _initialized;

    public static void AddSearchPath(string path)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        if (!_initialized)
        {
            if (!SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_USER_DIRS))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            _initialized = true;
        }

        var cookie = AddDllDirectory(path);
        if (cookie == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
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
