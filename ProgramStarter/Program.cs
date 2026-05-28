// SPDX-License-Identifier: Apache-2.0

using ProgramStarter;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Any(IsHelpArg))
{
    PrintHelp();
    return 0;
}

var startNow = args.Any(arg => arg.Equals("--start-now", StringComparison.OrdinalIgnoreCase));
var dryRun = args.Any(arg => arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
var visible = args.Any(arg => arg.Equals("--visible", StringComparison.OrdinalIgnoreCase));
var folder = GetFolder(args) ?? AppContext.BaseDirectory;

if (!startNow && !dryRun)
{
    PrintInfo();
    return 0;
}

var logPath = Path.Combine(folder, "program-starter.log");
Directory.CreateDirectory(folder);
using var log = new StreamWriter(File.Open(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
void Write(string message)
{
    var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}";
    log.WriteLine(line);
    log.Flush();
    if (visible || dryRun)
        Console.WriteLine(message);
}

using var mutex = new Mutex(false, GetMutexName(folder));
if (!mutex.WaitOne(TimeSpan.Zero))
{
    Write($"Another ProgramStarter run is already active for {folder}.");
    return 3;
}

try
{
    var manifest = ReadManifest(Path.Combine(folder, "program-starter.json"));
    var entries = Directory.EnumerateFiles(folder)
        .Where(IsRunnableFile)
        .Select(path => StartupEntryParser.TryParse(path, manifest, out var entry, out var reason)
            ? (Entry: entry, Reason: (string?)null)
            : (Entry: (StartupEntry?)null, Reason: $"{Path.GetFileName(path)} skipped: {reason}"))
        .ToList();

    foreach (var skipped in entries.Where(entry => entry.Reason != null))
        Write(skipped.Reason!);

    var runnable = entries
        .Where(entry => entry.Entry != null)
        .Select(entry => entry.Entry!)
        .OrderBy(entry => entry.Delay)
        .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    Write($"ProgramStarter found {runnable.Count} startup item(s).");
    if (dryRun)
    {
        foreach (var entry in runnable)
            Write($"{entry.Delay:c} {entry.DisplayName} -> {entry.Path}");
        return 0;
    }

    var startedAt = DateTimeOffset.Now;
    var failures = 0;
    foreach (var entry in runnable)
    {
        var targetTime = startedAt + entry.Delay;
        var remaining = targetTime - DateTimeOffset.Now;
        if (remaining > TimeSpan.Zero)
            Thread.Sleep(remaining);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = entry.Path,
                UseShellExecute = true,
            };
            if (entry.WindowStyle.HasValue)
                startInfo.WindowStyle = entry.WindowStyle.Value;

            Process.Start(startInfo);
            Write($"Started {entry.DisplayName} after {entry.Delay:c}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            failures++;
            Write($"Failed to start {entry.DisplayName}: {ex.Message}");
        }
    }

    Write(failures == 0 ? "ProgramStarter finished." : $"ProgramStarter finished with {failures} failure(s).");
    return failures == 0 ? 0 : 2;
}
finally
{
    mutex.ReleaseMutex();
}

static bool IsHelpArg(string arg)
    => arg.Equals("-h", StringComparison.OrdinalIgnoreCase)
       || arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
       || arg.Equals("/?", StringComparison.OrdinalIgnoreCase);

static bool IsRunnableFile(string path)
    => Path.GetFileName(path) is not "program-starter.json" and not "program-starter.log"
       && Path.GetExtension(path).ToLowerInvariant() is ".lnk" or ".url" or ".cmd" or ".ps1" or ".bat" or ".exe";

static string? GetFolder(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].Equals("--folder", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            return args[i + 1];
        if (args[i].StartsWith("--folder=", StringComparison.OrdinalIgnoreCase))
            return args[i]["--folder=".Length..];
    }

    return null;
}

static ProgramStarterManifest? ReadManifest(string path)
{
    if (!File.Exists(path))
        return null;

    try
    {
        return JsonSerializer.Deserialize<ProgramStarterManifest>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (JsonException)
    {
        return null;
    }
    catch (IOException)
    {
        return null;
    }
}

static string GetMutexName(string folder)
{
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(folder).ToUpperInvariant())));
    return @"Local\MoveShortcuts.ProgramStarter." + hash[..16];
}

static void PrintInfo()
{
    Console.WriteLine("""
        MoveShortcuts ProgramStarter

        This small runner is managed by MoveShortcuts. Windows starts it at logon
        with --start-now, and it launches timed shortcuts from its own folder.

        It is not meant to be configured by opening this executable directly.
        Use these commands instead:

          mvshct startup status
          mvshct startup run
        """);
}

static void PrintHelp()
{
    Console.WriteLine("""
        ProgramStarter

        Usage:
          ProgramStarter --start-now --folder <folder> [--visible]
          ProgramStarter --dry-run --folder <folder>

        Files are read from --folder. Names can start with delays such as
        45s_App.lnk or 01m30s_App.lnk. MoveShortcuts also writes
        program-starter.json with managed entries and launch preferences.
        """);
}
