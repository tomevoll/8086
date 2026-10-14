using System;
using System.Collections.Generic;

namespace Amiga500.Chipset;

public class SchedulerEvent
{
    public long TargetCycle { get; set; }
    public Action Callback { get; set; }
    public string Name { get; set; }

    public SchedulerEvent(long targetCycle, Action callback, string name = "")
    {
        TargetCycle = targetCycle;
        Callback = callback;
        Name = name;
    }
}

public class HardwareScheduler
{
    public long CurrentCycle { get; private set; } = 0; // Total color clocks elapsed
    private readonly List<SchedulerEvent> _events = new();

    public void ScheduleEvent(long delayCycles, Action callback, string name = "")
    {
        long target = CurrentCycle + delayCycles;
        var evt = new SchedulerEvent(target, callback, name);

        // Keep sorted by TargetCycle
        int idx = _events.FindIndex(e => e.TargetCycle > target);
        if (idx == -1) _events.Add(evt);
        else _events.Insert(idx, evt);
    }

    public void CancelEvents(string name)
    {
        _events.RemoveAll(e => e.Name == name);
    }

    public void AdvanceCycles(long cycles)
    {
        long endCycle = CurrentCycle + cycles;

        while (_events.Count > 0 && _events[0].TargetCycle <= endCycle)
        {
            var next = _events[0];
            _events.RemoveAt(0);
            CurrentCycle = next.TargetCycle;
            next.Callback.Invoke();
        }

        CurrentCycle = endCycle;
    }

    public void Reset()
    {
        CurrentCycle = 0;
        _events.Clear();
    }
}
