namespace MoveShortcuts
{
    public class Settings
    {
        public string shortcuts = @"C:\Shortcuts";

        public bool? getFavIcon = false;

        public Dictionary<string, MyFileOptions> fileOptions = new();

        public static Dictionary<string, MyFileOptions> defaultFileOptions = new()
        {
            // Default examples on how to use the settings file.

            // Microsof Group
            { @"Microsoft Edge", new() {
                AltNames = { "edge", "browser", "internet" },
            } },
            { @"Excel", new() {
                AltNames = { "ms-ex", "ms-sheets", "mse", "ex", "e" },
            } },
            { @"Word", new() {
                AltNames = { "ms-word", "ms-w", "msw", "wrd", "wd", "w" },
            } },

            // Android Apps on Windows via WSA
            { @"Play Store", new() {
                AltNames = { "play-store", "google-play", "gplay", "g-play", "play-store", "gplay-store", "playstore", "gstor", "store-google" },
            } },

            // Installed Web Apps
            { @"YouTube", new() {
                AltNames = { "yt" },
            } },
            { @"Gmail", new() {
                AltNames = { "gm", "gml", "eml", "mail", "email", "e-mail", "gmail", "g-mail" },
            } },

            // Games
            { @"StarCraft", new() {
                AltNames = { "sc", "sc1", "sc-1" },
            } },
            { @"Steam", new() {
                AltNames = { "stm", "st", "stem" },
            } },

            // Other Programs Group
            { @"CPUID CPU-Z", new() {
                AltNames = { "cpu", "cpuz", "cpus", "cpu-z", "cpuid" },
            } },
            { @"Everything", new() {
                AltNames = { "fnd", "find", "search" },
            } },
            { @"Firefox", new() {
                AltNames = { "ff", "ffox", "firef", "finternet", "foxinternet", "internet-fox", "internet-moz", "moz-internet" },
            } },
            { @"Git Extensions", new() {
                AltNames = { "gitex", "gitext", "gitextension", "gitextensions", "git-ex", "git-ext", "git-extension", "git-extensions" },
            } },
            { @"Google Chrome", new() {
                AltNames = { "chrome", "google-chrome" },
            } },
            { @"Google Drive", new() {
                AltNames = { "drv", "drive", "gdrive", "g-drive", "g-drv", "gdrv" },
            } },
            { @"Telegram", new() {
                AltNames = { "tel", "tg", "tgram", "tele", "teleg", "tlegram", "telgram", "tlgram" },
            } },
            { @"Visual Studio Code", new() {
                AltNames = { "code", "vscode", "vs-code" },
                ElevNames = { "coda", "vscoda", "vs-coda", "acode", "avscode", "a-code", "code-a", "a-vscode", "vscode-a" },
            } },
            { @"WhatsApp", new() {
                AltNames = { "whats", "whaz", "whazap", "whapp", "wzap", "zap", "zapzap", "zz", "wa" },
            } },
        };
    }
}
