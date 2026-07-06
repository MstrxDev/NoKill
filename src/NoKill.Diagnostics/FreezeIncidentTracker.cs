namespace NoKill.Diagnostics;

/// <summary>Tuning for freeze-incident detection.</summary>
public sealed record FreezeTrackerOptions
{
    /// <summary>A process must stay hung this long before an incident is declared. Debounces transient stalls.</summary>
    public TimeSpan ConfirmAfter { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Minimum gap between announced incidents for the SAME process. Zero disables the cooldown.</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(2);
}

public enum FreezeEventKind
{
    /// <summary>A confirmed freeze incident began (fires once per incident).</summary>
    Started,

    /// <summary>The frozen process recovered or exited.</summary>
    Ended,
}

public sealed record FreezeEvent(FreezeEventKind Kind, int ProcessId, string ProcessName, TimeSpan HungFor);

/// <summary>One tick's worth of facts about a process: is it hung right now.</summary>
public sealed record ProcessObservation(int ProcessId, string ProcessName, bool IsHung);

/// <summary>
/// Pure per-process freeze state machine, fed one observation batch per poll.
/// No timers, no Win32 — callers own the clock, which makes every debounce
/// and cooldown rule unit-testable. Rules: a freeze only becomes an incident
/// after persisting for ConfirmAfter; an ongoing incident never re-fires; a
/// process that recovers (or exits) re-arms for future incidents, subject to
/// the per-process cooldown.
/// </summary>
public sealed class FreezeIncidentTracker
{
    private sealed class PidState
    {
        public required string Name;
        public required DateTimeOffset HungSince;
        public bool IncidentAnnounced;
    }

    private readonly FreezeTrackerOptions _options;
    private readonly Dictionary<int, PidState> _states = [];
    private readonly Dictionary<int, DateTimeOffset> _cooldowns = [];

    public FreezeIncidentTracker(FreezeTrackerOptions? options = null)
    {
        _options = options ?? new FreezeTrackerOptions();
    }

    public IReadOnlyList<FreezeEvent> Observe(
        DateTimeOffset now, IReadOnlyList<ProcessObservation> observations)
    {
        var events = new List<FreezeEvent>();
        var hungNow = new HashSet<int>();

        foreach (var obs in observations.Where(o => o.IsHung))
        {
            hungNow.Add(obs.ProcessId);

            if (!_states.TryGetValue(obs.ProcessId, out var state))
            {
                _states[obs.ProcessId] = new PidState { Name = obs.ProcessName, HungSince = now };
                continue;
            }

            if (state.IncidentAnnounced)
            {
                continue; // ongoing incident: never re-fire
            }

            if (now - state.HungSince >= _options.ConfirmAfter)
            {
                bool inCooldown =
                    _options.Cooldown > TimeSpan.Zero
                    && _cooldowns.TryGetValue(obs.ProcessId, out var lastStart)
                    && now - lastStart < _options.Cooldown;

                state.IncidentAnnounced = true; // active either way, so Ended bookkeeping stays consistent
                if (!inCooldown)
                {
                    _cooldowns[obs.ProcessId] = now;
                    events.Add(new FreezeEvent(
                        FreezeEventKind.Started, obs.ProcessId, obs.ProcessName, now - state.HungSince));
                }
                else
                {
                    state.IncidentAnnounced = false; // suppressed by cooldown; stay silent but keep watching
                    state.HungSince = now;           // restart the confirm window
                }
            }
        }

        // Anything we were tracking that is no longer hung (or gone) has recovered.
        foreach (int pid in _states.Keys.Where(pid => !hungNow.Contains(pid)).ToList())
        {
            var state = _states[pid];
            if (state.IncidentAnnounced)
            {
                events.Add(new FreezeEvent(
                    FreezeEventKind.Ended, pid, state.Name, now - state.HungSince));
            }

            _states.Remove(pid);
        }

        return events;
    }

    /// <summary>Forget all in-flight state (cooldowns survive so toggling can't bypass them).</summary>
    public void Reset() => _states.Clear();
}
