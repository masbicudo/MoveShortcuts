// SPDX-License-Identifier: Apache-2.0

using Microsoft.Win32;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace MoveShortcuts
{
    public class UwpAppCache
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public DateTime createdUtc;
        public DateTime updatedUtc;
        public string machineName = "";
        public string userName = "";
        public string userSid = "";
        public UwpPackageSignature packageSignature = new();
        public List<UwpCachedApp> apps = new();
    }

    public class UwpCachedApp
    {
        public string name = "";
        public string appUserModelId = "";
    }

    public class UwpPackageSignature
    {
        public string source = UwpAppCacheProvider.AppModelRegistrySource;
        public int packageCount;
        public string hash = "";
    }

    public static class UwpAppCacheProvider
    {
        public const string DefaultCacheFileName = "move-shortcuts-uwp-cache.json";
        public const string AppModelRegistrySource = "HKCU AppModel Repository Packages";
        private const string AppModelRepositoryPackagesSubkey =
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

        public static Dictionary<string, string> GetUwpApps(
            string cachePath,
            bool refresh,
            Func<Dictionary<string, string>> enumerateApps,
            Action<string>? warn = null)
        {
            var signature = CreatePackageSignature();
            var identity = CreateCurrentIdentity();

            if (!refresh)
            {
                var cached = TryReadValidCache(cachePath, signature, identity, warn);
                if (cached != null)
                    return ToAppDictionary(cached.apps);
            }

            var apps = enumerateApps();
            TryWriteCache(cachePath, apps, signature, identity, warn);
            return apps;
        }

        public static UwpPackageSignature CreatePackageSignature()
        {
            var packageNames = GetAppModelPackageNames().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            var joined = string.Join("\n", packageNames);
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));

            return new UwpPackageSignature
            {
                packageCount = packageNames.Count,
                hash = "sha256:" + Convert.ToHexString(hashBytes).ToLowerInvariant(),
            };
        }

        public static List<string> GetAppModelPackageNames()
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppModelRepositoryPackagesSubkey);
            return key?.GetSubKeyNames().ToList() ?? new List<string>();
        }

        public static UwpCacheIdentity CreateCurrentIdentity()
            => new()
            {
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                UserSid = WindowsIdentity.GetCurrent().User?.Value ?? "",
            };

        public static UwpAppCache? TryReadValidCache(
            string cachePath,
            UwpPackageSignature signature,
            UwpCacheIdentity identity,
            Action<string>? warn = null)
        {
            UwpAppCache? cache;
            try
            {
                if (!File.Exists(cachePath))
                    return null;

                cache = JsonConvert.DeserializeObject<UwpAppCache>(File.ReadAllText(cachePath));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                warn?.Invoke($"Ignoring UWP cache: {ex.Message}");
                return null;
            }

            if (cache == null)
                return null;

            return IsCacheValid(cache, signature, identity) ? cache : null;
        }

        public static bool IsCacheValid(
            UwpAppCache cache,
            UwpPackageSignature signature,
            UwpCacheIdentity identity)
            => cache.schemaVersion == UwpAppCache.CurrentSchemaVersion
                && cache.machineName.Equals(identity.MachineName, StringComparison.OrdinalIgnoreCase)
                && cache.userName.Equals(identity.UserName, StringComparison.OrdinalIgnoreCase)
                && cache.userSid.Equals(identity.UserSid, StringComparison.OrdinalIgnoreCase)
                && cache.packageSignature.packageCount == signature.packageCount
                && cache.packageSignature.hash.Equals(signature.hash, StringComparison.OrdinalIgnoreCase);

        public static void TryWriteCache(
            string cachePath,
            Dictionary<string, string> apps,
            UwpPackageSignature signature,
            UwpCacheIdentity identity,
            Action<string>? warn = null)
        {
            try
            {
                var now = DateTime.UtcNow;
                var cache = new UwpAppCache
                {
                    createdUtc = now,
                    updatedUtc = now,
                    machineName = identity.MachineName,
                    userName = identity.UserName,
                    userSid = identity.UserSid,
                    packageSignature = signature,
                    apps = apps
                        .Select(kv => new UwpCachedApp { name = kv.Key, appUserModelId = kv.Value })
                        .ToList(),
                };

                var json = JsonConvert.SerializeObject(cache, Formatting.Indented);
                var directory = Path.GetDirectoryName(Path.GetFullPath(cachePath));
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var tempPath = cachePath + ".tmp";
                File.WriteAllText(tempPath, json);
                if (File.Exists(cachePath))
                    File.Replace(tempPath, cachePath, null);
                else
                    File.Move(tempPath, cachePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                warn?.Invoke($"Could not write UWP cache: {ex.Message}");
            }
        }

        private static Dictionary<string, string> ToAppDictionary(IEnumerable<UwpCachedApp> apps)
        {
            var result = new Dictionary<string, string>();
            foreach (var app in apps)
            {
                if (string.IsNullOrWhiteSpace(app.name))
                    continue;

                result[app.name] = app.appUserModelId;
            }

            return result;
        }
    }

    public class UwpCacheIdentity
    {
        public string MachineName { get; init; } = "";
        public string UserName { get; init; } = "";
        public string UserSid { get; init; } = "";
    }
}
