using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Winhance.Core.Features.Common.Constants;
using Winhance.Core.Features.Common.Interfaces;

namespace Winhance.Infrastructure.Features.Common.Services;

public class NewBadgeService : INewBadgeService
{
    private readonly IUserPreferencesService _prefs;
    private readonly ILogService _logService;
    private Version _baseline = new(99, 99, 99);

    public NewBadgeService(IUserPreferencesService prefs, ILogService logService)
    {
        _prefs = prefs;
        _logService = logService;
    }

    public bool ShowNewBadges
    {
        get => _prefs.GetPreference(UserPreferenceKeys.ShowNewBadges, true);
        set => _prefs.SetPreferenceAsync(UserPreferenceKeys.ShowNewBadges, value);
    }

    public void Initialize(IEnumerable<string?> allAddedInVersions)
    {
        // Keep writing LastRunVersion for future migration use — it no longer drives badges.
        var currentAssemblyVersion = GetAppVersion();
        _prefs.SetPreferenceAsync("LastRunVersion", currentAssemblyVersion);

        // Compute the highest AddedInVersion present in the loaded registry.
        Version? highestInRegistry = null;
        if (allAddedInVersions != null)
        {
            foreach (var raw in allAddedInVersions)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                if (!TryParseVersion(raw, out var parsed))
                    continue;
                if (highestInRegistry is null || parsed > highestInRegistry)
                    highestInRegistry = parsed;
            }
        }

        var storedHighestStr = _prefs.GetPreference(
            UserPreferenceKeys.HighestSeenAddedInVersion, "");

        // Branch A: no HighestSeenAddedInVersion stored — either a first-ever install or
        // a returning user whose preferences predate the badge system. Both get the same
        // treatment: baseline = 0.0.0 so every tagged setting shows as NEW. Without this,
        // a first-install user would see no badges at all and the View menu's NEW toggle
        // would appear inert.
        if (string.IsNullOrEmpty(storedHighestStr))
        {
            _baseline = new Version(0, 0, 0);
            if (highestInRegistry is not null)
            {
                _prefs.SetPreferenceAsync(
                    UserPreferenceKeys.HighestSeenAddedInVersion,
                    VersionToString(highestInRegistry));
            }
            _prefs.SetPreferenceAsync("NewBadgeBaseline", VersionToString(_baseline));
            // Do NOT touch ShowNewBadges — leave whatever the user already has.
            _logService.LogInformation(
                "[NewBadge] No stored HighestSeenAddedInVersion. Baseline set to 0.0.0 (all tagged settings treated as new).");
            return;
        }

        var stored = TryParseVersion(storedHighestStr, out var s) ? s : new Version(0, 0, 0);

        // Branch B: effective upgrade detected — new settings added to the registry since last run.
        if (highestInRegistry is not null && highestInRegistry > stored)
        {
            _baseline = stored;
            _prefs.SetPreferenceAsync(
                UserPreferenceKeys.HighestSeenAddedInVersion,
                VersionToString(highestInRegistry));
            _prefs.SetPreferenceAsync("NewBadgeBaseline", VersionToString(stored));
            ShowNewBadges = true;
            _logService.LogInformation(
                $"[NewBadge] Effective upgrade: registry highest {highestInRegistry} > stored {stored}. " +
                $"Baseline={stored}; ShowNewBadges reset to true.");
            return;
        }

        // Branch C: no upgrade since last run — use the stored NewBadgeBaseline so NEW
        // badges persist across app launches until the next upgrade. Falling back to
        // `stored` would advance the baseline every run and hide badges immediately
        // after the first post-upgrade launch.
        var storedBaselineStr = _prefs.GetPreference("NewBadgeBaseline", "");
        _baseline = TryParseVersion(storedBaselineStr, out var b) ? b : stored;
        _logService.LogDebug(
            $"[NewBadge] No upgrade. Baseline={_baseline}, ShowNewBadges={ShowNewBadges}.");
    }

    public bool IsSettingNew(string? addedInVersion, string settingId)
    {
        if (string.IsNullOrEmpty(addedInVersion))
            return false;

        if (!TryParseVersion(addedInVersion, out var settingVersion))
            return false;

        return settingVersion > _baseline;
    }

    private static string GetAppVersion()
    {
        var attr = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = attr?.InformationalVersion ?? "0.0.0";
        // Strip leading 'v' and any '+commithash' suffix
        version = version.TrimStart('v');
        var plusIndex = version.IndexOf('+');
        if (plusIndex >= 0)
            version = version[..plusIndex];
        return version;
    }

    private static bool TryParseVersion(string versionStr, out Version parsed)
    {
        if (string.IsNullOrWhiteSpace(versionStr))
        {
            parsed = new Version(0, 0, 0);
            return false;
        }
        versionStr = versionStr.Trim().TrimStart('v');
        return Version.TryParse(versionStr, out parsed!);
    }

    private static string VersionToString(Version v)
    {
        // Version.Build is -1 when not specified; normalise to 0.
        var build = v.Build < 0 ? 0 : v.Build;
        return $"{v.Major}.{v.Minor}.{build}";
    }
}
