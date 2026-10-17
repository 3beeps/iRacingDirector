using System;

namespace IRacingDirector.Core;

public enum SessionState
{
    /// <summary>The sim is not running.</summary>
    Idle,
    /// <summary>The sim is running.</summary>
    InSession,
    /// <summary>The sim closed recently; waiting out the end delay before declaring the session over.</summary>
    Ending,
}

public enum SessionEvent
{
    None,
    Started,
    /// <summary>The sim came back during the end delay (e.g. hopped into the next session).</summary>
    Resumed,
    Ended,
}

/// <summary>Turns "is the sim running?" samples into session start/end events.</summary>
public sealed class SessionTracker
{
    public SessionState State { get; private set; } = SessionState.Idle;
    public DateTime? EndingSince { get; private set; }
    public TimeSpan EndDelay { get; set; }

    public SessionTracker(TimeSpan endDelay) => EndDelay = endDelay;

    public SessionEvent Update(bool simRunning, DateTime now)
    {
        switch (State)
        {
            case SessionState.Idle when simRunning:
                State = SessionState.InSession;
                return SessionEvent.Started;

            case SessionState.InSession when !simRunning:
                if (EndDelay <= TimeSpan.Zero) return End();
                State = SessionState.Ending;
                EndingSince = now;
                return SessionEvent.None;

            case SessionState.Ending when simRunning:
                State = SessionState.InSession;
                EndingSince = null;
                return SessionEvent.Resumed;

            case SessionState.Ending when now - EndingSince >= EndDelay:
                return End();

            default:
                return SessionEvent.None;
        }
    }

    /// <summary>Seconds left before an ending session is declared over.</summary>
    public int SecondsUntilEnd(DateTime now) =>
        State == SessionState.Ending && EndingSince is { } since
            ? Math.Max(0, (int)Math.Ceiling((EndDelay - (now - since)).TotalSeconds))
            : 0;

    private SessionEvent End()
    {
        State = SessionState.Idle;
        EndingSince = null;
        return SessionEvent.Ended;
    }
}
