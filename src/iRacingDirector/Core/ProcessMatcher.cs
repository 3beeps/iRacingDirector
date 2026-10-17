using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace IRacingDirector.Core;

/// <summary>Case-insensitive process-name matching with '*' and '?' wildcards.</summary>
public static class ProcessMatcher
{
    public static bool Matches(string processName, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        if (pattern.IndexOfAny(new[] { '*', '?' }) < 0)
            return string.Equals(processName, pattern, StringComparison.OrdinalIgnoreCase);

        var regex = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return Regex.IsMatch(processName, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool MatchesAny(string processName, IEnumerable<string> patterns) =>
        patterns.Any(p => Matches(processName, p));
}
