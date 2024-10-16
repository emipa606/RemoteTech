﻿using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace RemoteTech;

/// <summary>
///     Calls a colonist to a detonator to perform the detonation.
/// </summary>
public class JobDriver_DetonateExplosives : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        return pawn.Reserve(job.targetA, job);
    }

    public override IEnumerable<Toil> MakeNewToils()
    {
        if (TargetThingA is not IPawnDetonateable detonator)
        {
            yield break;
        }

        var pathEndMode = detonator.UseInteractionCell ? PathEndMode.InteractionCell : PathEndMode.ClosestTouch;
        AddFailCondition(JobHasFailed);
        yield return Toils_Goto.GotoCell(TargetIndex.A, pathEndMode);
        yield return new Toil
        {
            initAction = () => detonator.DoDetonation(),
            defaultCompleteMode = ToilCompleteMode.Instant
        };
    }

    private bool JobHasFailed()
    {
        var detonator = TargetThingA as IPawnDetonateable;
        return detonator == null || ((Building)detonator).Destroyed || !detonator.WantsDetonation;
    }
}