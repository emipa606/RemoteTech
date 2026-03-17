using System;
using Verse;

namespace RemoteTech;

/// <summary>
///     Represents a value that is cached for a given number of ticks.
///     Allows to reduce performance overhead for getting expensive to calculate values every tick or frame.
///     Can be implicitly cast to its contained value.
/// </summary>
public class CachedValue<T>
{
    private readonly int recacheIntervalTicks;
    private readonly int tickOffset;

    private readonly Func<T> valueGetter;
    private int cachedTick;
    private T cachedValue;

    public CachedValue(Func<T> valueGetter, int recacheIntervalTicks = GenTicks.TicksPerRealSecond,
        bool useTickOffset = true)
    {
        this.valueGetter = valueGetter;
        this.recacheIntervalTicks = recacheIntervalTicks;
        cachedTick = int.MinValue;
        cachedValue = default;
        if (useTickOffset)
        {
            tickOffset = Rand.Range(0, recacheIntervalTicks);
        }
    }

    public T Value
    {
        get
        {
            if (!IsValid)
            {
                throw new InvalidOperationException($"{nameof(CachedValue<>)} cannot get Value: not initialized");
            }

            var currentTick = GenTicks.TicksGame;
            if (cachedTick + recacheIntervalTicks - tickOffset < currentTick)
            {
                Recache();
            }

            return cachedValue;
        }
    }

    public T ValueRecached
    {
        get
        {
            Recache();
            return Value;
        }
    }

    private bool IsValid => valueGetter != null;

    public static implicit operator T(CachedValue<T> val)
    {
        return val.Value;
    }

    public void Recache()
    {
        cachedTick = GenTicks.TicksGame;
        cachedValue = valueGetter();
    }
}