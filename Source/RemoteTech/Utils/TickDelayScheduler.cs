using System;
using System.Collections.Generic;
using Verse;

namespace RemoteTech;

public class TickDelayScheduler
{
    private readonly LinkedList<SchedulerEntry> entries = [];

    //public int lastProcessedTick = -1;

    public int lastProcessedTick;

    internal TickDelayScheduler()
    {
    }

    internal void Initialize(int currentTick)
    {
        //Log.Message("Initialize");
        lastProcessedTick = currentTick;
        //Log.Message($"Last processed tick: {lastProcessedTick}");
        entries.Clear();
    }

    internal void Tick(int currentTick)
    {
        if (lastProcessedTick < 0)
        {
            throw new Exception("Ticking not initialized TickDelayScheduler");
        }

        lastProcessedTick = currentTick;
        while (entries.First != null)
        {
            var value = entries.First.Value;
            if (value.dueAtTick > currentTick)
            {
                break;
            }

            entries.RemoveFirst();
            if (!value.repeat || !DoCallback(value))
            {
                continue;
            }

            value.dueAtTick = currentTick + value.interval;
            ScheduleEntry(value);
        }
    }

    /// <summary>
    ///     Registers a delegate to be called in a given number of ticks.
    /// </summary>
    /// <param name="callback">The delegate to be called</param>
    /// <param name="dueInTicks">The delay in ticks before the delegate is called</param>
    /// <param name="owner">Optional owner of the delegate. Callback will not fire if the Thing is not spawned at call time.</param>
    /// <param name="repeat">If true, the callback will be rescheduled after each call until manually unscheduled</param>
    public void ScheduleCallback(Action callback, int dueInTicks, Thing owner = null, bool repeat = false)
    {
        if (lastProcessedTick < 0)
        {
            Log.Message($"lastProcessedTick: {lastProcessedTick}");
            throw new Exception("Adding callback to not initialized TickDelayScheduler");
        }

        if (callback == null)
        {
            throw new NullReferenceException("callback cannot be null");
        }

        if (dueInTicks < 0)
        {
            throw new Exception("invalid dueInTicks value: " + dueInTicks);
        }

        if (dueInTicks == 0 && repeat)
        {
            throw new Exception("Cannot schedule repeating callback with 0 delay");
        }

        var schedulerEntry = new SchedulerEntry(callback, dueInTicks, owner, lastProcessedTick + dueInTicks, repeat);
        if (dueInTicks == 0)
        {
            DoCallback(schedulerEntry);
        }
        else
        {
            ScheduleEntry(schedulerEntry);
        }
    }

    /// <summary>
    ///     Manually remove a callback to abort a delay or clear a recurring callback.
    ///     Silently fails if the callback is not found.
    /// </summary>
    /// <param name="callback">The scheduled callback</param>
    public void TryUnscheduleCallback(Action callback)
    {
        for (var linkedListNode = entries.First; linkedListNode != null; linkedListNode = linkedListNode.Next)
        {
            if (linkedListNode.Value.callback != callback)
            {
                continue;
            }

            entries.Remove(linkedListNode);
            break;
        }
    }

    /// <summary>
    ///     Only for debug purposes
    /// </summary>
    public IEnumerable<SchedulerEntry> GetAllPendingCallbacks()
    {
        return entries;
    }

    private static bool DoCallback(SchedulerEntry entry)
    {
        if (entry.owner is { Spawned: false })
        {
            return false;
        }

        try
        {
            entry.callback();
            return true;
        }
        catch (Exception)
        {
            Log.Error("TickDelayScheduler caught an exception while calling {0} registered by {1}: {2}");
        }

        return false;
    }

    private void ScheduleEntry(SchedulerEntry newEntry)
    {
        for (var linkedListNode = entries.Last; linkedListNode != null; linkedListNode = linkedListNode.Previous)
        {
            if (linkedListNode.Value.dueAtTick > newEntry.dueAtTick)
            {
                continue;
            }

            entries.AddAfter(linkedListNode, newEntry);
            return;
        }

        entries.AddFirst(newEntry);
    }

    public class SchedulerEntry(Action callback, int interval, Thing owner, int dueAtTick, bool repeat)
    {
        public readonly Action callback = callback;

        public readonly int interval = interval;

        public readonly Thing owner = owner;

        public readonly bool repeat = repeat;

        public int dueAtTick = dueAtTick;
    }
}