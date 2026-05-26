$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $PSScriptRoot "output"
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ShlwapiIndirectString
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int SHLoadIndirectString(string source, StringBuilder output, uint outputLength, IntPtr reserved);
}
"@

# Derive the package family name used in AUMIDs from a package full name.
# Example: Microsoft.WindowsCalculator_11.0.0.0_x64__8wekyb3d8bbwe
# becomes Microsoft.WindowsCalculator_8wekyb3d8bbwe.
function Get-PackageFamilyNameFromFullName {
    param([Parameter(Mandatory = $true)][string]$FullName)

    $parts = $FullName -split "_"
    if ($parts.Length -lt 5) {
        return $null
    }

    return (($parts[0..($parts.Length - 5)] -join "_") + "_" + $parts[-1])
}

# Split a package-style AUMID into its package family component.
function Get-PackageFamilyNameFromAumid {
    param([string]$Aumid)

    if ([string]::IsNullOrWhiteSpace($Aumid) -or -not $Aumid.Contains("!")) {
        return $null
    }

    return ($Aumid -split "!", 2)[0]
}

# Split a package-style AUMID into the application ID after the bang.
function Get-AppIdFromAumid {
    param([string]$Aumid)

    if ([string]::IsNullOrWhiteSpace($Aumid) -or -not $Aumid.Contains("!")) {
        return $null
    }

    return ($Aumid -split "!", 2)[1]
}

# Break package full names into stable-ish fields so the analyzer can measure
# which embedded components explain AppsFolder matches.
function Split-PackageFullName {
    param([string]$FullName)

    $parts = $FullName -split "_"
    if ($parts.Length -lt 5) {
        return [pscustomobject]@{
            PackageName = $FullName
            Version = ""
            Architecture = ""
            ResourceId = ""
            PublisherId = ""
        }
    }

    return [pscustomobject]@{
        PackageName = ($parts[0..($parts.Length - 5)] -join "_")
        Version = $parts[$parts.Length - 4]
        Architecture = $parts[$parts.Length - 3]
        ResourceId = $parts[$parts.Length - 2]
        PublisherId = $parts[$parts.Length - 1]
    }
}

# Normalize raw registry values into comparable strings without interpreting
# Windows resource references.
function Normalize-RegistryString {
    param($Value)

    if ($null -eq $Value) {
        return $null
    }

    return ([string]$Value).Trim().Trim('"')
}

# Resolve one Windows indirect string with SHLoadIndirectString. This is the
# bridge that turns many ms-resource references into localized names.
function Invoke-IndirectStringResolver {
    param([string]$Source)

    if ([string]::IsNullOrWhiteSpace($Source)) {
        return $null
    }

    try {
        $buffer = New-Object System.Text.StringBuilder 2048
        $hr = [ShlwapiIndirectString]::SHLoadIndirectString($Source, $buffer, $buffer.Capacity, [IntPtr]::Zero)
        if ($hr -eq 0 -and $buffer.Length -gt 0 -and $buffer.ToString() -ne $Source) {
            return $buffer.ToString()
        }
    }
    catch {
        return $null
    }

    return $null
}

# Convert registry or manifest display-name values into best-effort user-facing
# names. It tries the package-full-name form first, then resources.pri.
function Resolve-DisplayString {
    param(
        [string]$Value,
        [string]$PackageFullName,
        [string]$PackageRootFolder
    )

    $normalized = Normalize-RegistryString $Value
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return ""
    }

    if ($normalized.StartsWith("@{", [StringComparison]::OrdinalIgnoreCase)) {
        $resolved = Invoke-IndirectStringResolver $normalized
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            return $resolved
        }
    }

    if ($normalized.IndexOf("ms-resource:", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        $resourceReference = $normalized

        if ($normalized.StartsWith("@{", [StringComparison]::OrdinalIgnoreCase)) {
            $question = $normalized.IndexOf("?")
            $closing = $normalized.LastIndexOf("}")
            if ($question -ge 0 -and $closing -gt $question) {
                $resourceReference = $normalized.Substring($question + 1, $closing - $question - 1)
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($PackageFullName)) {
            $resolved = Invoke-IndirectStringResolver "@{$PackageFullName`?$resourceReference}"
            if (-not [string]::IsNullOrWhiteSpace($resolved)) {
                return $resolved
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($PackageRootFolder)) {
            $priPath = Join-Path $PackageRootFolder "resources.pri"
            if (Test-Path -LiteralPath $priPath) {
                $resolved = Invoke-IndirectStringResolver "@{$priPath`?$resourceReference}"
                if (-not [string]::IsNullOrWhiteSpace($resolved)) {
                    return $resolved
                }
            }
        }
    }

    return $normalized
}

# Simple exact-name normalization used for before/after display-name diagnostics.
function Normalize-Name {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    return ($Value.Trim().Trim('"').ToLowerInvariant() -replace "\s+", " ")
}

# Produce accent-free, punctuation-free text for fuzzy matching.
function Get-SearchText {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $normalized = $Value.Normalize([Text.NormalizationForm]::FormD)
    $builder = New-Object System.Text.StringBuilder
    foreach ($ch in $normalized.ToCharArray()) {
        $category = [Globalization.CharUnicodeInfo]::GetUnicodeCategory($ch)
        if ($category -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$builder.Append($ch)
        }
    }

    return ($builder.ToString().ToLowerInvariant() -replace "[^a-z0-9]+", " ").Trim()
}

# Tokenize text after search normalization.
function Get-Tokens {
    param([string]$Value)

    $searchText = Get-SearchText $Value
    if ([string]::IsNullOrWhiteSpace($searchText)) {
        return @()
    }

    return @($searchText -split "\s+" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

# Compute Jaccard similarity between normalized token sets.
function Get-TokenJaccard {
    param([string]$Left, [string]$Right)

    $leftTokens = @(Get-Tokens $Left)
    $rightTokens = @(Get-Tokens $Right)
    if ($leftTokens.Count -eq 0 -or $rightTokens.Count -eq 0) {
        return 0.0
    }

    $leftSet = @{}
    foreach ($token in $leftTokens) { $leftSet[$token] = $true }
    $rightSet = @{}
    foreach ($token in $rightTokens) { $rightSet[$token] = $true }

    $intersection = 0
    foreach ($token in $leftSet.Keys) {
        if ($rightSet.ContainsKey($token)) {
            $intersection++
        }
    }

    $union = $leftSet.Count + $rightSet.Count - $intersection
    if ($union -eq 0) {
        return 0.0
    }

    return $intersection / $union
}

# Measure how much of the AppsFolder-name token set appears in a candidate name.
function Get-TokenContainment {
    param([string]$Needle, [string]$Haystack)

    $needleTokens = @(Get-Tokens $Needle)
    $haystackTokens = @(Get-Tokens $Haystack)
    if ($needleTokens.Count -eq 0 -or $haystackTokens.Count -eq 0) {
        return 0.0
    }

    $haystackSet = @{}
    foreach ($token in $haystackTokens) { $haystackSet[$token] = $true }

    $contained = 0
    foreach ($token in $needleTokens) {
        if ($haystackSet.ContainsKey($token)) {
            $contained++
        }
    }

    return $contained / $needleTokens.Count
}

# Standard dynamic-programming Levenshtein distance. Kept dependency-free so
# this research can run on a plain Windows/.NET/Python workstation.
function Get-LevenshteinDistance {
    param([string]$Left, [string]$Right)

    if ($null -eq $Left) { $Left = "" }
    if ($null -eq $Right) { $Right = "" }

    $n = $Left.Length
    $m = $Right.Length
    if ($n -eq 0) { return $m }
    if ($m -eq 0) { return $n }

    $previous = New-Object int[] ($m + 1)
    $current = New-Object int[] ($m + 1)
    for ($j = 0; $j -le $m; $j++) {
        $previous[$j] = $j
    }

    for ($i = 1; $i -le $n; $i++) {
        $current[0] = $i
        for ($j = 1; $j -le $m; $j++) {
            $cost = if ($Left[$i - 1] -eq $Right[$j - 1]) { 0 } else { 1 }
            $current[$j] = [Math]::Min(
                [Math]::Min($current[$j - 1] + 1, $previous[$j] + 1),
                $previous[$j - 1] + $cost)
        }

        $tmp = $previous
        $previous = $current
        $current = $tmp
    }

    return $previous[$m]
}

# Convert Levenshtein distance into a 0..1 similarity score on normalized text.
function Get-LevenshteinRatio {
    param([string]$Left, [string]$Right)

    $leftText = Get-SearchText $Left
    $rightText = Get-SearchText $Right
    $maxLength = [Math]::Max($leftText.Length, $rightText.Length)
    if ($maxLength -eq 0) {
        return 1.0
    }

    $distance = Get-LevenshteinDistance $leftText $rightText
    return 1.0 - ($distance / $maxLength)
}

# Bucket numeric similarity scores for information-gain calculations.
function Get-ScoreBin {
    param([double]$Score)

    if ($Score -ge 1.0) { return "1.00" }
    if ($Score -ge 0.75) { return "0.75-0.99" }
    if ($Score -ge 0.50) { return "0.50-0.74" }
    if ($Score -gt 0.0) { return "0.01-0.49" }
    return "0"
}

# Extract cheap structural features from strings: length, token count, and
# punctuation/digit flags.
function Get-TokenFeatures {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [pscustomobject]@{
            Length = 0
            TokenCount = 0
            HasDot = $false
            HasDash = $false
            HasDigit = $false
            StartsWithMsResource = $false
            StartsWithAtResource = $false
        }
    }

    $tokens = @($Value -split "[^A-Za-z0-9]+")
    $tokens = @($tokens | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    return [pscustomobject]@{
        Length = $Value.Length
        TokenCount = $tokens.Count
        HasDot = $Value.Contains(".")
        HasDash = $Value.Contains("-")
        HasDigit = ($Value -match "\d")
        StartsWithMsResource = $Value.StartsWith("ms-resource:", [StringComparison]::OrdinalIgnoreCase)
        StartsWithAtResource = $Value.StartsWith("@{", [StringComparison]::OrdinalIgnoreCase)
    }
}

# Bucket lengths so sparse exact lengths do not dominate information-gain tables.
function Get-BinnedLength {
    param([int]$Length)

    if ($Length -eq 0) { return "0" }
    if ($Length -le 4) { return "1-4" }
    if ($Length -le 12) { return "5-12" }
    if ($Length -le 32) { return "13-32" }
    return "33+"
}

# Classify package roots into broad locations that often separate launchable
# apps from system/internal packages.
function Get-RootKind {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return "empty" }
    if ($Path -like "C:\Windows\SystemApps*") { return "SystemApps" }
    if ($Path -like "C:\Program Files\WindowsApps*") { return "WindowsApps" }
    if ($Path -like "$env:LOCALAPPDATA\Packages*") { return "LocalPackages" }
    if ($Path -like "$env:LOCALAPPDATA*") { return "LocalAppData" }
    return "Other"
}

# Read AppxManifest.xml and extract Application IDs plus display metadata. This
# is the main source for candidates missed by registry subkeys/defaults.
function Get-ManifestApplications {
    param([string]$PackageRootFolder)

    if ([string]::IsNullOrWhiteSpace($PackageRootFolder)) {
        return @()
    }

    $manifestPath = Join-Path $PackageRootFolder "AppxManifest.xml"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return @()
    }

    try {
        [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw -ErrorAction Stop
        $namespaceManager = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
        $namespaceManager.AddNamespace("m", $manifest.DocumentElement.NamespaceURI)
        $namespaceManager.AddNamespace("uap", "http://schemas.microsoft.com/appx/manifest/uap/windows10")
        $applications = $manifest.SelectNodes("//m:Applications/m:Application", $namespaceManager)
        return @($applications | ForEach-Object {
            $visualElements = $_.SelectSingleNode("uap:VisualElements", $namespaceManager)
            [pscustomobject]@{
                Id = [string]$_.Id
                Executable = [string]$_.Executable
                DisplayName = if ($visualElements) { [string]$visualElements.DisplayName } else { "" }
            }
        })
    }
    catch {
        return @()
    }
}

Write-Host "Collecting AppsFolder entries..."
# AppsFolder is the label source: it is slow, but it is the shell view the user
# actually sees and launches from.
$appsFolder = (New-Object -ComObject Shell.Application).NameSpace("shell:::{4234d49b-0245-4df3-b780-3893943456e1}").Items() |
    ForEach-Object {
        $aumid = [string]$_.Path
        [pscustomobject]@{
            Name = [string]$_.Name
            Aumid = $aumid
            IsPackageStyleAumid = $aumid.Contains("!")
            PackageFamilyName = Get-PackageFamilyNameFromAumid $aumid
            AppId = Get-AppIdFromAumid $aumid
        }
    }

$appsFolderPackageAumids = @{}
$appsFolderPackageFamilies = @{}
foreach ($app in $appsFolder) {
    if ($app.IsPackageStyleAumid) {
        $appsFolderPackageAumids[$app.Aumid] = $app
        if (-not [string]::IsNullOrWhiteSpace($app.PackageFamilyName)) {
            $appsFolderPackageFamilies[$app.PackageFamilyName] = $true
        }
    }
}

Write-Host "Collecting AppModel registry package candidates..."
# The AppModel repository is the fast source being investigated. It is treated
# as an implementation detail, so the research measures how well it predicts
# AppsFolder instead of assuming it is authoritative.
$registryRoot = "HKCU:\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"
$registryCandidates = New-Object System.Collections.Generic.List[object]

if (Test-Path $registryRoot) {
    foreach ($package in Get-ChildItem -LiteralPath $registryRoot) {
        $packageFullName = $package.PSChildName
        $packageParts = Split-PackageFullName $packageFullName
        $packageNameFeatures = Get-TokenFeatures $packageParts.PackageName
        $packageFamilyName = Get-PackageFamilyNameFromFullName $packageFullName
        if ([string]::IsNullOrWhiteSpace($packageFamilyName)) {
            continue
        }

        $packageProperties = Get-ItemProperty -LiteralPath $package.PSPath -ErrorAction SilentlyContinue
        $packageRootFolder = Normalize-RegistryString $packageProperties.PackageRootFolder
        $packageDisplayName = Resolve-DisplayString $packageProperties.DisplayName $packageFullName $packageRootFolder
        $packageDisplayFeatures = Get-TokenFeatures $packageDisplayName
        $children = @(Get-ChildItem -LiteralPath $package.PSPath -ErrorAction SilentlyContinue)
        $manifestApplications = @(Get-ManifestApplications $packageRootFolder)
        $manifestApplicationIds = @{}
        foreach ($manifestApplication in $manifestApplications) {
            if (-not [string]::IsNullOrWhiteSpace($manifestApplication.Id)) {
                $manifestApplicationIds[$manifestApplication.Id] = $manifestApplication
            }
        }

        foreach ($child in $children) {
            $appIdFeatures = Get-TokenFeatures $child.PSChildName
            $capabilitiesPath = Join-Path $child.PSPath "Capabilities"
            $capabilities = $null
            $hasCapabilities = Test-Path $capabilitiesPath
            if ($hasCapabilities) {
                $capabilities = Get-ItemProperty -LiteralPath $capabilitiesPath -ErrorAction SilentlyContinue
            }

            $urlAssociationsPath = Join-Path $capabilitiesPath "URLAssociations"
            $candidateAumid = "$packageFamilyName!$($child.PSChildName)"
            $capabilitiesApplicationName = Resolve-DisplayString $capabilities.ApplicationName $packageFullName $packageRootFolder
            $capabilitiesApplicationDescription = Resolve-DisplayString $capabilities.ApplicationDescription $packageFullName $packageRootFolder
            $registryCandidates.Add([pscustomobject]@{
                PackageFullName = $packageFullName
                PackageFamilyName = $packageFamilyName
                PackageName = $packageParts.PackageName
                PackageArchitecture = $packageParts.Architecture
                PackageResourceId = $packageParts.ResourceId
                PublisherId = $packageParts.PublisherId
                PackageRootFolder = $packageRootFolder
                PackageRootKind = Get-RootKind $packageRootFolder
                PackageDisplayName = $packageDisplayName
                PackageNameLengthBin = Get-BinnedLength $packageNameFeatures.Length
                PackageNameTokenCount = $packageNameFeatures.TokenCount
                PackageNameHasDot = $packageNameFeatures.HasDot
                PackageNameHasDash = $packageNameFeatures.HasDash
                PackageDisplayNameLengthBin = Get-BinnedLength $packageDisplayFeatures.Length
                PackageDisplayNameTokenCount = $packageDisplayFeatures.TokenCount
                PackageDisplayNameHasDot = $packageDisplayFeatures.HasDot
                PackageDisplayNameHasDash = $packageDisplayFeatures.HasDash
                ChildCount = $children.Count
                AppSubkeyName = $child.PSChildName
                CandidateRule = "subkey"
                CandidateAumid = $candidateAumid
                CandidateAppId = $child.PSChildName
                CandidateSource = "registry-subkey"
                HasManifestApplication = $manifestApplicationIds.ContainsKey($child.PSChildName)
                ManifestDisplayName = if ($manifestApplicationIds.ContainsKey($child.PSChildName)) { $manifestApplicationIds[$child.PSChildName].DisplayName } else { "" }
                ManifestExecutable = if ($manifestApplicationIds.ContainsKey($child.PSChildName)) { $manifestApplicationIds[$child.PSChildName].Executable } else { "" }
                CandidateAppIdLengthBin = Get-BinnedLength $appIdFeatures.Length
                CandidateAppIdTokenCount = $appIdFeatures.TokenCount
                CandidateAppIdHasDot = $appIdFeatures.HasDot
                CandidateAppIdHasDash = $appIdFeatures.HasDash
                CandidateAppIdHasDigit = $appIdFeatures.HasDigit
                HasCapabilities = $hasCapabilities
                CapabilitiesApplicationName = $capabilitiesApplicationName
                CapabilitiesApplicationDescription = $capabilitiesApplicationDescription
                HasUrlAssociations = Test-Path $urlAssociationsPath
                IsDefaultCandidate = $false
            }) | Out-Null
        }

        if ($children.Count -eq 0) {
            # Many packages expose their launchable application as !App without
            # an explicit registry app subkey, so create conservative defaults.
            foreach ($defaultAppId in @("App", "HostedApp")) {
                $candidateAumid = "$packageFamilyName!$defaultAppId"
                $appIdFeatures = Get-TokenFeatures $defaultAppId
                $registryCandidates.Add([pscustomobject]@{
                    PackageFullName = $packageFullName
                    PackageFamilyName = $packageFamilyName
                    PackageName = $packageParts.PackageName
                    PackageArchitecture = $packageParts.Architecture
                    PackageResourceId = $packageParts.ResourceId
                    PublisherId = $packageParts.PublisherId
                    PackageRootFolder = $packageRootFolder
                    PackageRootKind = Get-RootKind $packageRootFolder
                    PackageDisplayName = $packageDisplayName
                    PackageNameLengthBin = Get-BinnedLength $packageNameFeatures.Length
                    PackageNameTokenCount = $packageNameFeatures.TokenCount
                    PackageNameHasDot = $packageNameFeatures.HasDot
                    PackageNameHasDash = $packageNameFeatures.HasDash
                    PackageDisplayNameLengthBin = Get-BinnedLength $packageDisplayFeatures.Length
                    PackageDisplayNameTokenCount = $packageDisplayFeatures.TokenCount
                    PackageDisplayNameHasDot = $packageDisplayFeatures.HasDot
                    PackageDisplayNameHasDash = $packageDisplayFeatures.HasDash
                    ChildCount = 0
                    AppSubkeyName = ""
                    CandidateRule = "no-subkey-$defaultAppId"
                    CandidateAumid = $candidateAumid
                    CandidateAppId = $defaultAppId
                    CandidateSource = "registry-default"
                    HasManifestApplication = $manifestApplicationIds.ContainsKey($defaultAppId)
                    ManifestDisplayName = if ($manifestApplicationIds.ContainsKey($defaultAppId)) { $manifestApplicationIds[$defaultAppId].DisplayName } else { "" }
                    ManifestExecutable = if ($manifestApplicationIds.ContainsKey($defaultAppId)) { $manifestApplicationIds[$defaultAppId].Executable } else { "" }
                    CandidateAppIdLengthBin = Get-BinnedLength $appIdFeatures.Length
                    CandidateAppIdTokenCount = $appIdFeatures.TokenCount
                    CandidateAppIdHasDot = $appIdFeatures.HasDot
                    CandidateAppIdHasDash = $appIdFeatures.HasDash
                    CandidateAppIdHasDigit = $appIdFeatures.HasDigit
                    HasCapabilities = $false
                    CapabilitiesApplicationName = ""
                    CapabilitiesApplicationDescription = ""
                    HasUrlAssociations = $false
                    IsDefaultCandidate = $true
                }) | Out-Null
            }
        }

        foreach ($manifestApplication in $manifestApplications) {
            # Manifest applications are noisier than registry subkeys, but they
            # provide the recall needed for real app IDs such as Netflix.App.
            if ([string]::IsNullOrWhiteSpace($manifestApplication.Id)) {
                continue
            }

            $candidateAumid = "$packageFamilyName!$($manifestApplication.Id)"
            $appIdFeatures = Get-TokenFeatures $manifestApplication.Id
            $registryCandidates.Add([pscustomobject]@{
                PackageFullName = $packageFullName
                PackageFamilyName = $packageFamilyName
                PackageName = $packageParts.PackageName
                PackageArchitecture = $packageParts.Architecture
                PackageResourceId = $packageParts.ResourceId
                PublisherId = $packageParts.PublisherId
                PackageRootFolder = $packageRootFolder
                PackageRootKind = Get-RootKind $packageRootFolder
                PackageDisplayName = $packageDisplayName
                PackageNameLengthBin = Get-BinnedLength $packageNameFeatures.Length
                PackageNameTokenCount = $packageNameFeatures.TokenCount
                PackageNameHasDot = $packageNameFeatures.HasDot
                PackageNameHasDash = $packageNameFeatures.HasDash
                PackageDisplayNameLengthBin = Get-BinnedLength $packageDisplayFeatures.Length
                PackageDisplayNameTokenCount = $packageDisplayFeatures.TokenCount
                PackageDisplayNameHasDot = $packageDisplayFeatures.HasDot
                PackageDisplayNameHasDash = $packageDisplayFeatures.HasDash
                ChildCount = $children.Count
                AppSubkeyName = ""
                CandidateRule = "manifest"
                CandidateAumid = $candidateAumid
                CandidateAppId = $manifestApplication.Id
                CandidateSource = "manifest"
                HasManifestApplication = $true
                ManifestDisplayName = Resolve-DisplayString $manifestApplication.DisplayName $packageFullName $packageRootFolder
                ManifestExecutable = Normalize-RegistryString $manifestApplication.Executable
                CandidateAppIdLengthBin = Get-BinnedLength $appIdFeatures.Length
                CandidateAppIdTokenCount = $appIdFeatures.TokenCount
                CandidateAppIdHasDot = $appIdFeatures.HasDot
                CandidateAppIdHasDash = $appIdFeatures.HasDash
                CandidateAppIdHasDigit = $appIdFeatures.HasDigit
                HasCapabilities = $false
                CapabilitiesApplicationName = ""
                CapabilitiesApplicationDescription = ""
                HasUrlAssociations = $false
                IsDefaultCandidate = $false
            }) | Out-Null
        }
    }
}

Write-Host "Labeling registry candidates against AppsFolder..."
# Each candidate row gets labels and similarity scores so the Python analyzer
# can compute feature information gain and precision/recall tables.
$candidateDataset = foreach ($candidate in $registryCandidates) {
    $matchedApp = $appsFolderPackageAumids[$candidate.CandidateAumid]
    $aumidExactMatch = $null -ne $matchedApp
    $registryBestName = if (-not [string]::IsNullOrWhiteSpace($candidate.CapabilitiesApplicationName)) {
        $candidate.CapabilitiesApplicationName
    } elseif (-not [string]::IsNullOrWhiteSpace($candidate.ManifestDisplayName)) {
        $candidate.ManifestDisplayName
    } else {
        $candidate.PackageDisplayName
    }
    $appsFolderName = if ($aumidExactMatch) { $matchedApp.Name } else { "" }
    $nameExactMatch = $aumidExactMatch -and ($appsFolderName -eq $registryBestName)
    $nameNormalizedMatch = $aumidExactMatch -and ((Normalize-Name $appsFolderName) -eq (Normalize-Name $registryBestName))
    $tokenJaccard = Get-TokenJaccard $appsFolderName $registryBestName
    $tokenContainment = Get-TokenContainment $appsFolderName $registryBestName
    $levenshteinRatio = Get-LevenshteinRatio $appsFolderName $registryBestName

    [pscustomobject]@{
        PackageFullName = $candidate.PackageFullName
        PackageFamilyName = $candidate.PackageFamilyName
        PackageName = $candidate.PackageName
        PackageArchitecture = $candidate.PackageArchitecture
        PackageResourceId = $candidate.PackageResourceId
        PublisherId = $candidate.PublisherId
        PackageRootFolder = $candidate.PackageRootFolder
        PackageRootKind = $candidate.PackageRootKind
        PackageDisplayName = $candidate.PackageDisplayName
        PackageNameLengthBin = $candidate.PackageNameLengthBin
        PackageNameTokenCount = $candidate.PackageNameTokenCount
        PackageNameHasDot = $candidate.PackageNameHasDot
        PackageNameHasDash = $candidate.PackageNameHasDash
        PackageDisplayNameLengthBin = $candidate.PackageDisplayNameLengthBin
        PackageDisplayNameTokenCount = $candidate.PackageDisplayNameTokenCount
        PackageDisplayNameHasDot = $candidate.PackageDisplayNameHasDot
        PackageDisplayNameHasDash = $candidate.PackageDisplayNameHasDash
        ChildCount = $candidate.ChildCount
        AppSubkeyName = $candidate.AppSubkeyName
        CandidateRule = $candidate.CandidateRule
        CandidateAumid = $candidate.CandidateAumid
        CandidateAppId = $candidate.CandidateAppId
        CandidateSource = $candidate.CandidateSource
        HasManifestApplication = $candidate.HasManifestApplication
        ManifestDisplayName = $candidate.ManifestDisplayName
        ManifestExecutable = $candidate.ManifestExecutable
        CandidateAppIdLengthBin = $candidate.CandidateAppIdLengthBin
        CandidateAppIdTokenCount = $candidate.CandidateAppIdTokenCount
        CandidateAppIdHasDot = $candidate.CandidateAppIdHasDot
        CandidateAppIdHasDash = $candidate.CandidateAppIdHasDash
        CandidateAppIdHasDigit = $candidate.CandidateAppIdHasDigit
        HasCapabilities = $candidate.HasCapabilities
        CapabilitiesApplicationName = $candidate.CapabilitiesApplicationName
        CapabilitiesApplicationDescription = $candidate.CapabilitiesApplicationDescription
        HasUrlAssociations = $candidate.HasUrlAssociations
        IsDefaultCandidate = $candidate.IsDefaultCandidate
        RegistryBestName = $registryBestName
        PackageFamilyAppearsInAppsFolder = $appsFolderPackageFamilies.ContainsKey($candidate.PackageFamilyName)
        AumidExactMatch = $aumidExactMatch
        AppsFolderName = $appsFolderName
        NameExactMatch = $nameExactMatch
        NameNormalizedMatch = $nameNormalizedMatch
        TokenJaccard = $tokenJaccard
        TokenJaccardBin = Get-ScoreBin $tokenJaccard
        TokenContainment = $tokenContainment
        TokenContainmentBin = Get-ScoreBin $tokenContainment
        LevenshteinRatio = $levenshteinRatio
        LevenshteinRatioBin = Get-ScoreBin $levenshteinRatio
        RegistryBestNameIsResource = ([string]$registryBestName).Contains("ms-resource:")
        PackageDisplayNameIsResource = ([string]$candidate.PackageDisplayName).Contains("ms-resource:")
    }
}

$candidateAumids = @{}
foreach ($candidate in $registryCandidates) {
    $candidateAumids[$candidate.CandidateAumid] = $true
}

$unmatchedAppsFolder = $appsFolder |
    Where-Object { $_.IsPackageStyleAumid -and -not $candidateAumids.ContainsKey($_.Aumid) }

$appsFolderPath = Join-Path $outputDir "appsfolder.csv"
$registryPath = Join-Path $outputDir "registry-candidates.csv"
$datasetPath = Join-Path $outputDir "candidate-dataset.csv"
$unmatchedPath = Join-Path $outputDir "appsfolder-unmatched.csv"

$appsFolder | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $appsFolderPath
$registryCandidates | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $registryPath
$candidateDataset | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $datasetPath
$unmatchedAppsFolder | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $unmatchedPath

Write-Host "Wrote:"
Write-Host "  $appsFolderPath ($($appsFolder.Count) rows)"
Write-Host "  $registryPath ($($registryCandidates.Count) rows)"
Write-Host "  $datasetPath ($(@($candidateDataset).Count) rows)"
Write-Host "  $unmatchedPath ($(@($unmatchedAppsFolder).Count) rows)"
