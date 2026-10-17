using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace IRacingDirector.Core;

/// <summary>A support application managed alongside iRacing.</summary>
public sealed class AppEntry : ObservableObject
{
    private string _name = "";
    private string _path = "";
    private string _arguments = "";
    private string _extraProcessNames = "";
    private bool _startWithSession = true;
    private bool _stopWithSession = true;
    private bool _startMinimized;
    private int _startDelaySeconds;
    private bool _isRunning;
    private bool _launchedByDirector;
    private string _statusText = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get => _name; set => SetField(ref _name, value); }

    /// <summary>Full path to the executable (or a shortcut / file to open).</summary>
    public string Path
    {
        get => _path;
        set { if (SetField(ref _path, value)) OnPropertyChanged(nameof(PathExists)); }
    }

    public string Arguments { get => _arguments; set => SetField(ref _arguments, value); }

    /// <summary>
    /// Comma separated extra process names (wildcards allowed) that belong to this app,
    /// e.g. "garage61-agent*". Used for running detection and closing.
    /// </summary>
    public string ExtraProcessNames { get => _extraProcessNames; set => SetField(ref _extraProcessNames, value); }

    public bool StartWithSession { get => _startWithSession; set => SetField(ref _startWithSession, value); }
    public bool StopWithSession { get => _stopWithSession; set => SetField(ref _stopWithSession, value); }
    public bool StartMinimized { get => _startMinimized; set => SetField(ref _startMinimized, value); }
    public int StartDelaySeconds { get => _startDelaySeconds; set => SetField(ref _startDelaySeconds, Math.Max(0, value)); }

    // ---- Runtime state (not persisted) ----

    [JsonIgnore] public bool IsRunning { get => _isRunning; set => SetField(ref _isRunning, value); }
    [JsonIgnore]
    public bool LaunchedByDirector
    {
        get => _launchedByDirector;
        set { if (value) LaunchedAt = DateTime.UtcNow; SetField(ref _launchedByDirector, value); }
    }
    [JsonIgnore] public DateTime LaunchedAt { get; private set; }
    [JsonIgnore] public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
    [JsonIgnore] public bool PathExists => File.Exists(Path) || Directory.Exists(Path);

    /// <summary>Process name of the app's own exe (matched together with its folder), or null if Path isn't an exe.</summary>
    [JsonIgnore]
    public string? MainProcessName =>
        !string.IsNullOrWhiteSpace(Path) && Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.GetFileNameWithoutExtension(Path)
            : null;

    /// <summary>
    /// Extra process-name patterns from <see cref="ExtraProcessNames"/>. Patterns that are too broad
    /// (fewer than 3 non-wildcard characters, like "*" or "a*") are dropped so a typo can't match every process.
    /// </summary>
    public IReadOnlyList<string> GetExtraPatterns() => ExtraProcessNames
        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p)
        .Where(ProcessMatcher.IsSpecificEnough)
        .ToList();

    /// <summary>All process-name patterns that identify this app.</summary>
    public IReadOnlyList<string> GetProcessPatterns()
    {
        var list = new List<string>();
        if (MainProcessName is { } main) list.Add(main);
        list.AddRange(GetExtraPatterns());
        return list;
    }

    public AppEntry Clone() => new()
    {
        Id = Id,
        Name = Name,
        Path = Path,
        Arguments = Arguments,
        ExtraProcessNames = ExtraProcessNames,
        StartWithSession = StartWithSession,
        StopWithSession = StopWithSession,
        StartMinimized = StartMinimized,
        StartDelaySeconds = StartDelaySeconds,
    };

    public void CopyFrom(AppEntry o)
    {
        Name = o.Name;
        Path = o.Path;
        Arguments = o.Arguments;
        ExtraProcessNames = o.ExtraProcessNames;
        StartWithSession = o.StartWithSession;
        StopWithSession = o.StopWithSession;
        StartMinimized = o.StartMinimized;
        StartDelaySeconds = o.StartDelaySeconds;
    }
}
