using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IRacingDirector.Core;

/// <summary>Case-insensitive process-name matching with '*' and '?' wildcards.</summary>
public static class ProcessMatcher
{
    /// <summary>
    /// Windows and iRacing processes Director never treats as part of an app, whatever the patterns say.
    /// </summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost",
        "dwm", "explorer", "sihost", "fontdrvhost", "ctfmon", "taskhostw", "RuntimeBroker", "ShellExperienceHost",
        "StartMenuExperienceHost", "SearchHost", "SearchIndexer", "dllhost", "spoolsv", "audiodg", "MsMpEng",
        "conhost", "iRacingService64", "iRacingDirector",
    };

    public static bool IsProtected(string processName) => Protected.Contains(processName);

    /// <summary>A pattern needs at least 3 real characters, so "*" or "a*" can't match everything.</summary>
    public static bool IsSpecificEnough(string pattern) =>
        !string.IsNullOrWhiteSpace(pattern) && pattern.Count(c => c != '*' && c != '?' && !char.IsWhiteSpace(c)) >= 3;

    public static bool Matches(string processName, string pattern)
    {
        if (!IsSpecificEnough(pattern)) return false;
        if (pattern.IndexOfAny(new[] { '*', '?' }) < 0)
            return string.Equals(processName, pattern, StringComparison.OrdinalIgnoreCase);

        var regex = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(processName, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool MatchesAny(string processName, IEnumerable<string> patterns) =>
        patterns.Any(p => Matches(processName, p));
}
