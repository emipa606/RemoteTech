using System.Collections.Generic;
using System.Linq;
using HugsLib.Utils;
using Verse;

namespace RemoteTech;

public static class SwitchableUtility
{
    private static IEnumerable<ISwitchable> SwitchablesOnThing(Thing thing)
    {
        var list = new List<ISwitchable>();
        if (thing is ISwitchable t)
        {
            list.Add(t);
        }

        if (thing is not ThingWithComps comps)
        {
            return list;
        }

        foreach (var comp in comps.AllComps)
        {
            if (comp is ISwitchable s)
            {
                list.Add(s);
            }
        }

        return list;
    }

    extension(Thing thing)
    {
        public void UpdateSwitchDesignation()
        {
            if (thing.Map == null)
            {
                return;
            }

            thing.ToggleDesignation(Resources.Designation.rxSwitchThing, thing.WantsSwitching());
        }

        public bool WantsSwitching()
        {
            return SwitchablesOnThing(thing).Any(s => s.WantsSwitch());
        }

        public void TrySwitch()
        {
            foreach (var s in SwitchablesOnThing(thing))
            {
                if (s.WantsSwitch())
                {
                    s.DoSwitch();
                }
            }
        }
    }
}