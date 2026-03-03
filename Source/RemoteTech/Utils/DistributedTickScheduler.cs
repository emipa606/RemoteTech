using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RemoteTech;

public class DistributedTickScheduler
{
    private readonly Dictionary<Thing, TickableEntry> entries = new();

    private readonly List<ListTicker> tickers = [];

    private readonly Queue<Thing> unregisterQueue = new();

    private int lastProcessedTick = -1;

    internal DistributedTickScheduler()
    {
    }

    public void RegisterTickability(Action callback, int tickInterval, Thing owner)
    {
        if (lastProcessedTick < 0)
        {
            throw new Exception("Adding callback to not initialized DistributedTickScheduler");
        }

        if (owner == null || owner.Destroyed)
        {
            throw new Exception("A non-null, not destroyed owner Thing is required to register for tickability");
        }

        if (tickInterval < 1)
        {
            throw new Exception("Invalid tick interval: " + tickInterval);
        }

        if (entries.ContainsKey(owner))
        {
            Log.Warning("DistributedTickScheduler tickability already registered for: " + owner);
            return;
        }

        var tickableEntry = new TickableEntry(callback, tickInterval, owner);
        GetTicker(tickInterval).Register(tickableEntry);
        entries.Add(owner, tickableEntry);
    }

    public void UnregisterTickability(Thing owner)
    {
        if (!IsRegistered(owner))
        {
            throw new ArgumentException("Cannot unregister non-registered owner: " + owner);
        }

        var tickableEntry = entries[owner];
        var ticker = GetTicker(tickableEntry.interval);
        ticker.Unregister(tickableEntry);
        if (ticker.EntryCount == 0)
        {
            tickers.Remove(ticker);
        }

        entries.Remove(owner);
    }

    public bool IsRegistered(Thing owner)
    {
        return entries.ContainsKey(owner);
    }

    public IEnumerable<TickableEntry> DebugGetAllEntries()
    {
        return entries.Values;
    }

    public int DebugCountLastTickCalls()
    {
        return tickers.Sum(t => t.NumCallsLastTick);
    }

    public int DebugGetNumTickers()
    {
        return tickers.Count;
    }

    internal void Initialize(int currentTick)
    {
        entries.Clear();
        tickers.Clear();
        lastProcessedTick = currentTick;
    }

    internal void Tick(int currentTick)
    {
        if (lastProcessedTick < 0)
        {
            throw new Exception("Ticking not initialized DistributedTickScheduler");
        }

        lastProcessedTick = currentTick;
        foreach (var listTicker in tickers)
        {
            listTicker.Tick(currentTick);
        }

        UnregisterQueuedOwners();
    }

    private void UnregisterAtEndOfTick(Thing owner)
    {
        unregisterQueue.Enqueue(owner);
    }

    private void UnregisterQueuedOwners()
    {
        while (unregisterQueue.Count > 0)
        {
            var owner = unregisterQueue.Dequeue();
            if (IsRegistered(owner))
            {
                UnregisterTickability(owner);
            }
        }
    }

    private ListTicker GetTicker(int interval)
    {
        foreach (var getTicker in tickers)
        {
            if (getTicker.tickInterval == interval)
            {
                return getTicker;
            }
        }

        var listTicker = new ListTicker(interval, this);
        tickers.Add(listTicker);
        return listTicker;
    }

    public class TickableEntry(Action callback, int interval, Thing owner)
    {
        public readonly Action callback = callback;

        public readonly int interval = interval;

        public readonly Thing owner = owner;
    }

    private class ListTicker(int tickInterval, DistributedTickScheduler scheduler)
    {
        public readonly int tickInterval = tickInterval;

        private readonly List<TickableEntry> tickList = [];

        private int currentIndex;

        private float listProgress;

        private int nextCycleStart;

        private bool tickInProgress;

        public int NumCallsLastTick { get; private set; }

        public int EntryCount => tickList.Count;

        public void Tick(int currentTick)
        {
            tickInProgress = true;
            NumCallsLastTick = 0;
            if (nextCycleStart <= currentTick)
            {
                currentIndex = 0;
                listProgress = 0f;
                nextCycleStart = currentTick + tickInterval;
            }

            listProgress += tickList.Count / (float)tickInterval;
            var num = Mathf.Min(tickList.Count, Mathf.CeilToInt(listProgress));
            while (currentIndex < num)
            {
                var tickableEntry = tickList[currentIndex];
                if (tickableEntry.owner.Spawned)
                {
                    try
                    {
                        tickableEntry.callback();
                        NumCallsLastTick++;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"DistributedTickScheduler caught an exception! {ex}");
                    }
                }
                else
                {
                    scheduler.UnregisterAtEndOfTick(tickableEntry.owner);
                }

                currentIndex++;
            }

            tickInProgress = false;
        }

        public void Register(TickableEntry entry)
        {
            AssertNotTicking();
            tickList.Add(entry);
        }

        public void Unregister(TickableEntry entry)
        {
            AssertNotTicking();
            tickList.Remove(entry);
        }

        private void AssertNotTicking()
        {
            if (tickInProgress)
            {
                throw new Exception("Cannot register or unregister a callback while a tick is in progress");
            }
        }
    }
}