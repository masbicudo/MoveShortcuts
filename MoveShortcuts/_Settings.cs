using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MoveShortcuts
{
    internal static class _Settings
    {
        public static string shortcuts = @"C:\Shortcuts";

        public static Dictionary<string, MyFileOptions> fileOptions = new()
        {
            { @"Microsoft Edge Dev", new() {
                AltNames = { "edge-dev", "edgedev", "edev", "e-dev", "edged", "edge-d" },
            } },
            { @"Dashboard — CartPanda", new() {
                AltNames = { "cartpanda", "cpanda", "cpand" },
            } },
            { @"Microsoft 365", new() {
                AltNames = { "ms365", "365", "office", "ms-office", "msoffice" },
            } },
            { @"Sizer", new() {
                AltNames = { "sz", "szr", "resizer", "rsz", "rszr" },
            } },

          // Microsof Office Group
            { @"Access", new() {
                AltNames = { "ms-acc", "ms-access", "msa", "acc" },
            } },
            { @"Excel", new() {
                AltNames = { "ms-ex", "ms-sheets", "mse", "ex", "e" },
            } },
            { @"OneDrive", new() {
                AltNames = { "odrive", "odrv", "o-d", "o-drv", "one", "msone", "ms-one", "onedrv", "one-drv", "oned", "one-d" },
            } },
            { @"Outlook", new() {
                AltNames = { "ms-out", "outlook", "olook", "ol", "msol", "mso" },
            } },
            { @"PowerPoint", new() {
                AltNames = { "ms-pp", "pwrpnt", "ppnt", "powerpnt", "mspp", "msp" },
            } },
            { @"Publisher", new() {
                AltNames = { "ms-pub", "pub", "mspub", "mspu" },
            } },
            { @"Word", new() {
                AltNames = { "ms-word", "ms-w", "msw", "wrd", "wd", "w" },
            } },
            { @"OneNote", new() {
                AltNames = { "ms-onenote", "ms-note", "ms-on", "mson", "msonote", "msonenote", "onote", "msnote" },
            } },

          // Android Apps on Windows via WSA
            { @"Amazon Appstore", new() {
                AltNames = { "amazon-store", "amazon-app-store", "aas", "aastore", "appstore", "app-store", "amazonstore", "amstor", "store-amazon" },
            } },
            { @"Play Store", new() {
                AltNames = { "play-store", "google-play", "gplay", "g-play", "play-store", "gplay-store", "playstore", "gstor", "store-google" },
            } },
            { @"Nubank", new() {
                AltNames = { "nu" },
            } },
            { @"BB", new() {
                AltNames = { "bb" },
            } },
            { @"Prime Video", new() {
                AltNames = { "pv", "apv", "prime", "pvideo", "primevideo", "amazon-pv", "amazonpv" },
            } },
            { @"Magisk", new() {
                AltNames = { "mgisk", "mgsk" },
            } },
            { @"C6 Bank", new() {
                AltNames = { "c6" },
            } },

          // Installed Web Apps
            { @"YouTube", new() {
                AltNames = { "yt" },
            } },
            { @"Twitch", new() {
                AltNames = { "tw", "tch", "ttch", "twtch", "twi", "twch", "twich" },
            } },
            { @"Gmail", new() {
                AltNames = { "gm", "gml", "eml", "mail", "email", "e-mail", "gmail", "g-mail" },
            } },
            { @"Google Photos", new() {
                AltNames = { "photos", "gphotos", "g-photos", "gfotos", "g-fotos" },
            } },
            { @"Google Calendar", new() {
                AltNames = { "cal", "calend", "calendar", "calendario", "gcal", "gcalend", "gcalendar", "gcalendario", "g-cal", "g-calend", "g-calendar", "g-calendario" },
            } },
            { @"Google Sala de Aula", new() {
                AltNames = { "cls", "classroom", "clrm", "clsrm", "clsroom", "class", "classes", "g-class", "g-classes", "gclass", "gclasses" },
            } },
            { @"Sheets", new() {
                AltNames = { "gs", "gsheets", "g-sheets" },
            } },
            { @"Slides", new() {
                AltNames = { "gsl", "gslides", "g-slides" },
            } },
            { @"Docs", new() {
                AltNames = { "gd", "gdocs", "g-docs" },
            } },
            { @"Google Translate", new() {
                AltNames = { "gt", "gtr", "translate" },
            } },
            { @"Google Sites", new() {
                AltNames = { "gs", "sites", "gsites", "g-sites" },
            } },
            { @"Google Forms", new() {
                AltNames = { "gf", "forms", "gforms", "g-forms" },
            } },
            { @"Blogger", new() {
                AltNames = { "blog", "blg" },
            } },
            { @"Google Drive WebApp", new() {
                AltNames = { "webdrv", "webdrive", "wgdrive", "wg-drive", "wg-drv", "wgdrv", "drvw", "driveweb", "gdriveweb", "g-drive-web", "g-drv-w", "gdrvw" },
            } },
            { @"Google Finance", new() {
                AltNames = { "fin", "gfin", "g-fin", "finance", "gfinance", "g-finance", "finances" },
            } },
            { @"Google Meet", new() {
                AltNames = { "gm", "gmt", "meet", "meets", "gmeet", "gmeets", "g-meet", "g-meets" },
            } },
            { @"Google Maps", new() {
                AltNames = { "maps", "gmaps", "g-maps", "g-map", "map", "gmap", "gmp" },
            } },
            { @"Colaboratory", new() {
                AltNames = { "colab", "clb" },
            } },
            { @"Odysee", new() {
                AltNames = { "od", "odysey", "odisey", "odyse", "odsee", "odse" },
            } },
            { @"Star+", new() {
                AltNames = { "sp", "star", "starplus", "star-plus", "s+", "splus", "stpl" },
            } },
            { @"GETTR", new() {
                AltNames = { "gtr", "gttr", "getr" },
            } },
            { @"Rumble", new() {
                AltNames = { "rmb", "rumb", "rmbl", "rmble", "rumbl", "rble" },
            } },
            { @"Google Images", new() {
                AltNames = { "images", "gimages", "g-images", "imgs" },
            } },
            { @"SIGA", new() {
                AltNames = { "siga" },
            } },
            { @"OLX", new() {
                AltNames = { "br-olx", "olx-br", "olx.br", "br.olx", "brolx", "olxbr" },
            } },
            { @"Mercado Livre", new() {
                AltNames = { "ml", "mlivre", "merclivr" },
            } },

          // Games
            { @"Battle.net", new() {
                AltNames = { "bnet", "b.net", "b-net" },
            } },
            { @"StarCraft II", new() {
                AltNames = { "sc2", "sc-2", "scii", "sc-ii" },
            } },
            { @"StarCraft", new() {
                AltNames = { "sc", "sc1", "sc-1" },
            } },
            { @"Steam", new() {
                AltNames = { "stm", "st", "stem" },
            } },
            { @"Wallpaper Engine", new() {
                AltNames = { "we", "wpp", "w-engine", "weng", "wengine", "wall-eng", "wallpp-engine", "wpeng", "wp-eng" },
            } },
            { @"Battlefield Bad Company™ 2", new() {
                AltNames = { "bf", "bfbc", "bf-bc", "bc", "bfbc2", "bfbc-2", "bf-bc2", "bf-bc-2", "bc2", "bc-2" },
            } },
            { @"Grand Theft Auto V", new() {
                AltNames = { "gta", "gta5", "gta-5", "gtav", "gta-v" },
            } },
            { @"Rockstar Games Launcher", new() {
                AltNames = { "rsgl", "rs-gl", "rockstar", "rgl", "r-gl" },
            } },
            { @"Roblox Player", new() {
                AltNames = { "rx", "roblox", "rbx", "rblox", "robox", "blox" },
            } },
            { @"Roblox Studio", new() {
                AltNames = { "rxs", "rx-studio", "rxstudio", "robloxstudio", "rbxs", "rbloxs", "roboxs", "bloxs" },
            } },
            { @"ScourgeBringer", new() {
                AltNames = { "sb" },
            } },
            { @"Recompile", new() {
                AltNames = { "rcp", "recomp", "rcmp", "rcomp", "recmp", "recp" },
            } },
            { @"Amazon Games", new() {
                AltNames = { "ag", "amzgames", "amzgms", "agames", "amazon-games", "a-games" },
            } },
            { @"Epic Games Launcher", new() {
                AltNames = { "epic", "epicgames", "epiclaucher" },
            } },
            { @"Rumbleverse", new() {
                AltNames = { "rv" },
            } },
            { @"Pinball FX", new() {
                AltNames = { "pinball", "pball", "pbfx", "pb-fx" },
            } },
            { @"Command Conquer Generals - Zero Hour", new() {
                AltNames = { "cczh", "cnczh", "cnc-zh", "zh", "zero-hour" },
            } },
            { @"Тoррент  Игpуxа", new() {
                AltNames = { "Torrent-Game" },
            } },
            { @"Command & Conquer™ Remastered Collection", new() {
                AltNames = { "cnc" },
            } },

            { @"Exodus", new() {
                AltNames = { "exd", "exd-wallet", "ex-wallet", "exw", "xw" },
            } },
            { @"Nimiq Wallet", new() {
                AltNames = { "nimiq", "nim", "nimiq-wallet", "nim-wallet", "nimwallet", "nimwal" },
            } },
            { @"NIMIQ.WATCH Pool", new() {
                AltNames = { "nimiq-watch", "nimwatch", "nimiq-watch-pool", "nim-watch-pool", "nimwatch-pool", "nim-watch", "nimpool", "nimwpool", "nimpl", "nimwpl", "nimp", "nimwp" },
            } },

          // Other Programs Group
            { @"7-Zip File Manager", new() {
                AltNames = { "7zui", "7zipui", "7-zipui", "7-zui", "7z-ui", "7zip-ui", "7-zip-ui", "7-z-ui", "7zgui", "7zipgui", "7-zipgui", "7-zgui", "7z-gui", "7zip-gui", "7-zip-gui", "7-z-gui" },
            } },
            { @"Adobe Acrobat DC", new() {
                AltNames = { "acrobat", "reader", "adobe-reader" },
            } },
            { @"AdwCleaner", new() {
                AltNames = { "adw", "adwc", "adw-c", "adw-clean", "adw-cleaner" },
            } },
            { @"AnyDesk", new() {
                AltNames = { "any", "anyd", "adesk", "any-desk", "any-d", "a-desk", "ad", "a-d" },
            } },
            { @"BleachBit", new() {
                AltNames = { "bleach", "clean" },
                ElevNames = { "bleacha", "cleana", "ableach", "aclean" },
            } },
            { @"balenaEtcher", new() {
                AltNames = { "be", "etcher", "betcher", "etch", "balena", "balena-etcher" },
            } },
            { @"CCleaner", new() {
                AltNames = { "cc", "cclean", "ccl" },
            } },
            { @"Chrome Remote Desktop", new() {
                AltNames = { "crdesk", "rdesk", "remdesk", "crd", "remote", "desktop", "chrome-remote-desktop", "chrome-desktop", "chrome-remote", "chrome-rd" },
            } },
            { @"CPUID CPU-Z", new() {
                AltNames = { "cpu", "cpuz", "cpus", "cpu-z", "cpuid" },
            } },
            { @"CrystalDiskInfo", new() {
                AltNames = { "cdi", "cdinfo", "cdiskinfo", "cdiski" },
            } },
            { @"CrystalDiskMark 8 Shizuku Edition (64bit)", new() {
                AltNames = { "cdm-se", "cdm8-se", "cdiskmark-se", "cdisk-se", "cdm", "cdm8", "cdiskmark", "cdisk" },
            } },
            { @"CrystalDiskInfo Shizuku Edition", new() {
                AltNames = { "cdm-se", "cdiskmark-se", "cdisk-se", "cdm", "cdiskmark", "cdisk" },
            } },
            { @"CrystalDiskMark 8", new() {
                AltNames = { "cdm", "cdm8", "cdiskmark", "cdisk" },
            } },
            { @"diagrams.net", new() {
                AltNames = { "dia", "diagrams", "dg.net", "dia.net", "dgnet", "dg", "dianet" },
            } },
            { @"Dashboard   UptimeRobot", new() {
                AltNames = { "UptimeRobot", "uptime", "ur", "urobot", "uptr" },
            } },
            { @"Discord", new() {
                AltNames = { "dsc", "disc", "dis", "dscrd", "dscord" },
            } },
            { @"Docker Desktop", new() {
                AltNames = { "docker-desktop", "dockdesk", "dkdk", "dkrdkt", "ddt" },
            } },
            { @"Dont Sleep", new() {
                AltNames = { "dont", "dsleep", "dontsleep", "dont-sleep", "no-sleep", "nosleep", "donsleep" },
            } },
            { @"Everything", new() {
                AltNames = { "fnd", "find", "search" },
            } },
            { @"Firefox", new() {
                AltNames = { "ff", "ffox", "firef", "finternet", "foxinternet", "internet-fox", "internet-moz", "moz-internet" },
            } },
            { @"FreeTube", new() {
                AltNames = { "ft", "youtube" },
            } },
            { @"GeForce Experience", new() {
                AltNames = { "gfex" },
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
            { @"grepWin", new() {
                AltNames = { "grep", "grep-win", "wgrep" },
                ElevNames = { "a-grep", "grep-a", "agrep", "grepa" },
            } },
            { @"GPU-Z", new() {
                AltNames = { "gpu", "gpuz", "gpus" },
            } },
            { @"HandBrake", new() {
                AltNames = { "hb", "hbrake", "hand-brake", "h-brake", "hbreak" },
            } },
            { @"HxD", new() {
                AltNames = { "hex" },
                ElevNames = { "aHxD", "ahex" },
            } },
            { @"HWiNFO64", new() {
                AltNames = { "hwi", "hwinfo", "hw", "hw64" },
            } },
            { @"ImageGlass", new() {
                AltNames = { "ig", "iglass", "igls", "iglas", "imageg" },
            } },
            { @"ImDisk Virtual Disk Driver", new() {
                AltNames = { "imdisk-driver", "imdisk-drv", "im-drive", "im-drv", "imdrive", "imdrv", "idrv" },
            } },
            { @"Montar arquivo de imagem", new() {
                AltNames = { "imdisk-mount", "im-mount", "immount", "im-mnt", "immnt", "imnt" },
            } },
            { @"Configuração RamDisk", new() {
                AltNames = { "ramdisk", "ram-disk", "ram-dsk", "ramdsk", "ram-d", "ramd", "imram", "imramd", "iram", "iramd" },
            } },
            { @"Inkscape", new() {
                AltNames = { "is", "iscape", "ink", "inks" },
            } },
            { @"Julia 1.7.2", new() {
                AltNames = { "jul", "jl" },
            } },
            { @"Julia 1.8.0", new() {
                AltNames = { "jul", "jl" },
            } },
            { @"Julia 1.8.3", new() {
                AltNames = { "jul", "jl" },
            } },
            { @"KDiff3", new() {
                AltNames = { "diff", "kdiff", "kdif", "dif", "kd3" },
            } },
            { @"LibreOffice 7.2", new() {
                AltNames = { "LibreOffice", "loffice", "libre", "libreo" },
            } },
            { @"LibreOffice 7.3", new() {
                AltNames = { "LibreOffice", "loffice", "libre", "libreo" },
            } },
            { @"Microsoft Edge", new() {
                AltNames = { "edge", "ms-edge", "msedge", "microsoft-edge", "msed", "msie", "msinternet", "ms-internet", "internet", "internet-edge", "internet-ms" },
            } },
            { @"Microsoft Teams (work or school)", new() {
                AltNames = { "teams", "msteams", "ms-teams", "msteam", "mst", "team" },
            } },
            { @"MPC-BE x64", new() {
                AltNames = { "mpc", "mpc-be", "mpcbe" },
            } },
            { @"MPC-HC x64", new() {
                AltNames = { "mpc-hc", "mpchc" },
            } },
            { @"MPC-QT", new() {
                AltNames = { "mpcqt" },
            } },
            { @"MSI Afterburner", new() {
                AltNames = { "msi-ab", "ab", "afterburner", "after-burner", "msi-after-burner", "msiab" },
            } },
            { @"MultiPar", new() {
                AltNames = { "mp", "par", "par2", "recovery-record" },
                ElevNames = { "aMultiPar", "amp", "apar", "apar2", "arecovery-record" },
            } },
            { @"Notepad\+\+", new() {
                AltNames = { "npp", "n++" },
                ElevNames = { "anpp", "nppa", "an++", "n++a", "npp-a", "a-npp", "n++-a", "a-n++" },
            } },
            { @"OBS Studio", new() {
                AltNames = { "obs", "obss" },
            } },
            { @"Oracle VM VirtualBox", new() {
                AltNames = { "vbox", "vb", "virtualbox", "virtbox", "vmbox" },
            } },
            { @"paint.net", new() {
                AltNames = { "pdn", "pnet", "p-net", "pdnet", "pd-net", "paintnet", "paint-net", "paintdotnet", "paint-dot-net" },
            } },
            { @"pgAdmin 4", new() {
                AltNames = { "pgadmin", "pgadm", "pga", "pa", "pgadmin4", "pgadm4", "pga4", "pa4" },
            } },
            { @"Photoshop", new() {
                AltNames = { "ps" },
            } },
            { @"Windows PowerShell", new() {
                AltNames = { "posh" },
                ElevNames = { "posha", "a-posh", "posh-a", "aposh" },
            } },
            { @"Premiere", new() {
                AltNames = { "pr" },
            } },
            { @"qBittorrent", new() {
                AltNames = { "qbt", "torrent", "qb" },
            } },
            { @"R 4.2.0", new() {
                AltNames = { "r", "r-lang", "rlang", "r-lng", "rlng" },
                ElevNames = { "ar-lang", "arlang", "ar-lng", "arlng" },
            } },
            { @"R 4.2.0 Patched", new() {
                AltNames = { "r", "r-lang", "rlang", "r-lng", "rlng" },
                ElevNames = { "ar-lang", "arlang", "ar-lng", "arlng" },
            } },
            { @"Resilio Sync", new() {
                AltNames = { "res", "resilio", "resil", "rsl", "res-sync", "rsls", "resync", "reslsync", "rslsync", "rlsync" },
            } },
            { @"Revo Uninstaller", new() {
                AltNames = { "revo", "uninstaller", "uninst", "uninstall", "revo-uninst", "revo-uninstaller" },
            } },
            { @"Send To Kindle", new() {
                AltNames = { "stk", "send2kindle", "s2k", "send-kindle", "s-k", "sendkindle", "sendk", "sk", "kindle" },
            } },
            { @"Speccy", new() {
                AltNames = { "spec", "specs" },
            } },
            { @"SSHFS-Win Manager", new() {
                AltNames = { "sshfs-win-manager", "sshfs-wm", "sshfs-manager", "sshfs-man", "manage-sshfs", "sfman", "sf-man", "ssh-fs", "sshfs" },
            } },
            { @"SumatraPDF", new() {
                AltNames = { "spdf", "sumatra", "ebook-reader", "epub-reader", "epub" },
            } },
            { @"Surfshark", new() {
                AltNames = { "ss", "shark", "sshark", "surfs" },
            } },
            { @"Synology Drive Client", new() {
                AltNames = { "sdrive", "s-drive", "syndrv", "syn-drv", "syndrive", "syn-drive" },
            } },
            { @"SyncTrayzor", new() {
                AltNames = { "sync-t", "synct", "sync", "synctray", "st", "stray", "strazor", "trazor" },
            } },
            { @"Tad", new() {
                AltNames = { "csv", "csv-viewer" },
            } },
            { @"Telegram", new() {
                AltNames = { "tel", "tg", "tgram", "tele", "teleg", "tlegram", "telgram", "tlgram" },
            } },
            { @"VirusTotal Uploader 2.2", new() {
                AltNames = { "vt", "vtu", "vtup", "vt-up", "virus-total", "virus-total-uploader" },
            } },
            { @"Visual Studio Code", new() {
                AltNames = { "code", "vscode", "vs-code" },
                ElevNames = { "coda", "vscoda", "vs-coda", "acode", "avscode", "a-code", "code-a", "a-vscode", "vscode-a" },
            } },
            { @"VLC media player", new() {
                AltNames = { "vlc", "vlcp", "vlcmp", "vmp", "player", "vlc-mp" },
            } },
            { @"WhatsApp", new() {
                AltNames = { "whats", "whaz", "whazap", "whapp", "wzap", "zap", "zapzap", "zz", "wa" },
            } },
            { @"WizTree", new() {
                AltNames = { "wiz", "wizt", "wiztr", "wzt", "wtree", "wztree", "wt" },
            } },
            { @"WinMerge", new() {
                AltNames = { "wm", "wmerge", "merge", "winmer", "wmer", "winmrg", "mrg", "wmerg", "wmrg" },
            } },
        };

    }
}
