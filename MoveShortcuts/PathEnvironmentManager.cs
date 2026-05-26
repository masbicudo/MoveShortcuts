// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.IO;
using System.Text;

namespace MoveShortcuts
{
    public enum PathPlacement
    {
        AppendIfMissing,
        PrependOrMove,
        AppendOrMove
    }

    public enum PathUpdateStatus
    {
        AlreadyPresent,
        Updated,
        CancelledOrFailed
    }

    public static class PathEnvironmentManager
    {
        public static bool IsDirectoryInPath(string directory, string? pathValue)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(pathValue))
                return false;

            var fullDirectory = Normalize(directory);
            return pathValue
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(entry => string.Equals(Normalize(entry), fullDirectory, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsDirectoryInTargetPath(string directory, EnvironmentVariableTarget target)
            => IsDirectoryInPath(directory, Environment.GetEnvironmentVariable("Path", target));

        public static PathUpdateStatus AddToUserPath(string directory, PathPlacement placement = PathPlacement.AppendIfMissing)
        {
            var fullDirectory = Path.GetFullPath(directory);
            var path = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
            var updated = BuildPathValue(path, fullDirectory, placement);
            if (string.Equals(path, updated, StringComparison.Ordinal))
                return PathUpdateStatus.AlreadyPresent;

            Environment.SetEnvironmentVariable("Path", updated, EnvironmentVariableTarget.User);
            return PathUpdateStatus.Updated;
        }

        public static PathUpdateStatus AddToMachinePathElevated(string directory, PathPlacement placement = PathPlacement.AppendIfMissing)
        {
            var fullDirectory = Path.GetFullPath(directory);
            var path = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
            var updated = BuildPathValue(path, fullDirectory, placement);
            if (string.Equals(path, updated, StringComparison.Ordinal))
                return PathUpdateStatus.AlreadyPresent;

            var script = CreateMachinePathScript(fullDirectory, placement);
            var scriptPath = Path.Combine(Path.GetTempPath(), "MoveShortcuts-add-machine-path-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(scriptPath, script, Encoding.UTF8);

            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                });

                if (process == null)
                    return PathUpdateStatus.CancelledOrFailed;

                process.WaitForExit();
                var currentPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
                return string.Equals(BuildPathValue(currentPath, fullDirectory, placement), currentPath, StringComparison.Ordinal)
                    ? PathUpdateStatus.Updated
                    : PathUpdateStatus.CancelledOrFailed;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return PathUpdateStatus.CancelledOrFailed;
            }
            finally
            {
                try
                {
                    File.Delete(scriptPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        public static string AppendPathEntry(string pathValue, string directory)
        {
            if (string.IsNullOrWhiteSpace(pathValue))
                return directory;

            return pathValue.TrimEnd(Path.PathSeparator) + Path.PathSeparator + directory;
        }

        public static string BuildPathValue(string pathValue, string directory, PathPlacement placement)
        {
            var fullDirectory = Normalize(directory);
            var originalEntries = pathValue
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var withoutDirectory = originalEntries
                .Where(entry => !string.Equals(Normalize(entry), fullDirectory, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var existing = withoutDirectory.Count != originalEntries.Count;

            if (existing && placement == PathPlacement.AppendIfMissing)
                return pathValue;

            return placement switch
            {
                PathPlacement.PrependOrMove => string.Join(Path.PathSeparator, new[] { directory }.Concat(withoutDirectory)),
                _ => string.Join(Path.PathSeparator, withoutDirectory.Concat(new[] { directory })),
            };
        }

        private static string CreateMachinePathScript(string directory, PathPlacement placement)
        {
            var escaped = directory.Replace("'", "''");
            var placementName = placement.ToString();
            return $$"""
                $ErrorActionPreference = 'Stop'
                $directory = '{{escaped}}'
                $placement = '{{placementName}}'
                $target = [EnvironmentVariableTarget]::Machine
                $path = [Environment]::GetEnvironmentVariable('Path', $target)
                $entries = @()
                if (-not [string]::IsNullOrWhiteSpace($path)) {
                    $entries = $path.Split([IO.Path]::PathSeparator, [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() }
                }
                $fullDirectory = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($directory)).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
                $kept = @($entries | Where-Object {
                    $entryFullPath = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($_)).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
                    -not [string]::Equals($entryFullPath, $fullDirectory, [StringComparison]::OrdinalIgnoreCase)
                })
                $exists = $kept.Count -ne $entries.Count
                if ($exists -and $placement -eq 'AppendIfMissing') {
                    return
                }
                if ($placement -eq 'PrependOrMove') {
                    $updatedEntries = @($directory) + $kept
                } else {
                    $updatedEntries = $kept + @($directory)
                }
                $updated = [string]::Join([IO.Path]::PathSeparator, $updatedEntries)
                [Environment]::SetEnvironmentVariable('Path', $updated, $target)
                """;
        }

        private static string Normalize(string path)
            => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
