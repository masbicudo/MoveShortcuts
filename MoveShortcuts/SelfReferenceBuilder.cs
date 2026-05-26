// SPDX-License-Identifier: Apache-2.0

using System.IO;

namespace MoveShortcuts
{
    public static class SelfReferenceBuilder
    {
        public const string DefaultCommandName = "mvshct";
        public const string DefaultEditCommandName = "mvshct-edit";

        public static Dictionary<string, MyFileOptions> CreateSelfReferenceOptions(
            string commandName,
            string editCommandName,
            string assemblyLocation,
            string? processPath)
        {
            var launch = CreateLaunchInfo(assemblyLocation, processPath);
            return new Dictionary<string, MyFileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                [commandName] = CreateCommandOption(launch.Target, launch.Arguments),
                [editCommandName] = CreateCommandOption(launch.Target, AppendArgument(launch.Arguments, "edit")),
            };
        }

        private static MyFileOptions CreateCommandOption(string target, string? arguments)
            => new()
            {
                Action = FileAction.FileLink,
                Target = target,
                Arguments = arguments,
                LinkTypes = { "cmd" },
            };

        private static SelfLaunchInfo CreateLaunchInfo(string assemblyLocation, string? processPath)
        {
            if (assemblyLocation.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(processPath)
                && File.Exists(processPath))
            {
                return new SelfLaunchInfo(processPath, Quote(assemblyLocation));
            }

            return new SelfLaunchInfo(assemblyLocation, null);
        }

        private static string AppendArgument(string? arguments, string argument)
            => string.IsNullOrWhiteSpace(arguments) ? argument : arguments + " " + argument;

        private static string Quote(string value)
            => "\"" + value.Replace("\"", "\\\"") + "\"";

        private sealed record SelfLaunchInfo(string Target, string? Arguments);
    }
}
