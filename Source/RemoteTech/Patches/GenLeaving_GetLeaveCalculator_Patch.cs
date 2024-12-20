﻿using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RemoteTech.Patches;

/// <summary>
///     Prevents the resource dropper from subtracting 1 from dropped resources when deconstructing explosives
///     This is hardcoded behavior, so we need a patch.
/// </summary>
[HarmonyPatch(typeof(GenLeaving), "GetBuildingResourcesLeaveCalculator", typeof(Thing), typeof(DestroyMode))]
internal static class GenLeaving_GetLeaveCalculator_Patch
{
    [HarmonyPostfix]
    public static void FullRefundOnDeconstruct(Thing destroyedThing, DestroyMode mode, ref Func<int, int> __result)
    {
        if (mode == DestroyMode.Deconstruct && destroyedThing?.def != null &&
            destroyedThing.def.HasModExtension<FullDeconstructionRefund>())
        {
            __result = count => count;
        }
    }
}