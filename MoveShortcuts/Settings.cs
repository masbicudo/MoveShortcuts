// SPDX-License-Identifier: Apache-2.0

namespace MoveShortcuts
{
    public class Settings
    {
        public string shortcuts = @"C:\Shortcuts";

        public bool? getFavIcon = false;

        public string? progress = "auto";

        public SourceSettings sources = new();

        public CleanupSettings cleanup = new();

        public AliasSettings aliases = new();

        public PathSettings path = new();

        public ProgramStarterSettings programStarter = new();

        public Dictionary<string, MyFileOptions> fileOptions = new();
    }

    public class SourceSettings
    {
        public bool desktop = true;
        public bool startMenu = true;
        public bool uwpApps = true;
    }

    public class CleanupSettings
    {
        public bool deleteDesktopShortcuts = false;
    }

    public class AliasSettings
    {
        public bool generateInitials = false;
        public int minimumLength = 2;
    }

    public class PathSettings
    {
        public bool addToUserPath = false;
        public bool addToMachinePath = false;
        public string userPathPlacement = "auto";
        public string machinePathPlacement = "auto";
    }

    public class ProgramStarterSettings
    {
        public bool enabled = false;
        public string folderName = "ProgramStarter";
        public bool installAtLogon = true;
        public string runnerWindow = "hidden";
    }
}
