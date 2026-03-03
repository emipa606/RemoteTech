using System;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RemoteTech;

// A mining explosive that is able to break thick mountain roof
// TODO: Add postfix to AutoBuildRoofZoneSetter to remove roof orders over collapsed rock, make collapsed rock impassable again
public class CompRoofBreakerExplosive : CompMiningExplosive
{
    private const int RoofFilthAmount = 3;
    private static TickDelayScheduler tickDelayScheduler;
    private readonly IntRange CollapseDelay = new(0, 120);

    protected override void Detonate()
    {
        var map = parentMap;
        var position = parentPosition;
        base.Detonate();
        if (map == null)
        {
            return;
        }

        if (props is not CompProperties_Explosive explosiveProps)
        {
            return;
        }

        var canAffectThickRoof =
            RemoteTechUtility.IsEffectiveRoofBreakerPlacement(explosiveProps.explosiveRadius, position, map, false);
        var anyThickRoofAffected = false;
        foreach (var cell in GenRadial.RadialCellsAround(position, explosiveProps.explosiveRadius, true))
        {
            if (!cell.InBounds(map))
            {
                continue;
            }

            var roof = map.roofGrid.RoofAt(cell);
            if (roof == null || roof.isThickRoof && !canAffectThickRoof)
            {
                continue;
            }

            if (roof.filthLeaving != null)
            {
                for (var j = 0; j < RoofFilthAmount; j++)
                {
                    FilthMaker.TryMakeFilth(cell, map, roof.filthLeaving);
                }
            }

            if (roof.isThickRoof)
            {
                anyThickRoofAffected = true;
                var roofCell = cell;

                // get instance
                var gameComponent = Current.Game.GetComponent<GameComponent_TickDelayScheduler>();
                if (gameComponent != null)
                {
                    tickDelayScheduler = gameComponent.scheduler;
                }

                // check if valid
                if (tickDelayScheduler == null || tickDelayScheduler.lastProcessedTick < 0)
                {
                    //Log.Message($"Last processed tick: {tickDelayScheduler?.lastProcessedTick}");
                    //Log.Warning("TickDelayScheduler is either null or not initialized.");
                    throw new Exception("TickDelayScheduler is either null or not initialized");
                }

                tickDelayScheduler.ScheduleCallback(() =>
                {
                    // delay collapse for more interesting visual effect
                    CollapseRockOnCell(roofCell, map);
                    SoundDefOf.Roof_Collapse.PlayOneShot(new TargetInfo(roofCell, map));
                }, CollapseDelay.RandomInRange);
            }

            map.roofGrid.SetRoof(cell, null);
        }

        if (anyThickRoofAffected)
        {
            Resources.Sound.rxMiningCavein.PlayOneShot(new TargetInfo(position, map));
        }
    }

    private static void CollapseRockOnCell(IntVec3 cell, Map map)
    {
        CrushThingsUnderCollapsingRock(cell, map);
        var rock = GenSpawn.Spawn(Resources.Thing.rxCollapsedRoofRocks, cell, map);
        if (rock.def.rotatable)
        {
            rock.Rotation = Rot4.Random;
        }
    }

    private static void CrushThingsUnderCollapsingRock(IntVec3 cell, Map map)
    {
        for (var i = 0; i < 2; i++)
        {
            var thingList = cell.GetThingList(map);
            for (var j = thingList.Count - 1; j >= 0; j--)
            {
                var thing = thingList[j];
                //map.roofCollapseBuffer.Notify_Crushed(thing);
                DamageInfo dinfo;
                if (thing is Pawn pawn)
                {
                    var brain = pawn.health.hediffSet.GetBrain();
                    dinfo = new DamageInfo(DamageDefOf.Crush, 99999, 1F, -1f, null, brain);
                }
                else
                {
                    dinfo = new DamageInfo(DamageDefOf.Crush, 99999);
                    dinfo.SetBodyRegion(BodyPartHeight.Top, BodyPartDepth.Outside);
                }

                thing.TakeDamage(dinfo);
                if (!thing.Destroyed && thing.def.destroyable)
                {
                    thing.Destroy();
                }
            }
        }
    }
}