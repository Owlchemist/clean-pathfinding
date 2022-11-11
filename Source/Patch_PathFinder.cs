using HarmonyLib;
using Verse;
using Verse.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using RimWorld;
using UnityEngine;
using static CleanPathfinding.CleanPathfindingUtility;
using static CleanPathfinding.ModSettings_CleanPathfinding;
 
namespace CleanPathfinding
{
    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.FindPath), new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode), typeof(PathFinderCostTuning) })]
    static class Patch_PathFinder
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var mapInfo = AccessTools.Field(typeof(PathFinder), nameof(PathFinder.map));
            var rangeInfo = AccessTools.Field(typeof(TerrainDef), nameof(TerrainDef.extraNonDraftedPerceivedPathCost));
            bool ran = false;
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
	            if (codes[i].opcode == OpCodes.Ldfld && codes[i].OperandIs(rangeInfo))
	            {
		            codes.InsertRange(i + 3, new List<CodeInstruction>(){

                        new CodeInstruction(OpCodes.Ldloc_0),
                        new CodeInstruction(OpCodes.Ldloc_S, 12), //topGrid
                        new CodeInstruction(OpCodes.Ldloc_S, 45), //TerrainDef within the grid
                        new CodeInstruction(OpCodes.Ldelem_Ref),
                        new CodeInstruction(OpCodes.Ldloc_S, 48), //Pathcost total
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldfld, mapInfo),
                        new CodeInstruction(OpCodes.Ldloc_S, 45), //cell location
                        new CodeInstruction(OpCodes.Call, typeof(Patch_PathFinder).GetMethod(nameof(Patch_PathFinder.AdjustCosts))),
                        new CodeInstruction(OpCodes.Stloc_S, 48)
                    });
                    ran = true;
                    break;
                }
            }
            if (!ran) Log.Warning("[Clean Pathfinding] Transpiler could not find target. There may be a mod conflict, or RimWorld updated?");
            return codes.AsEnumerable();
        }

        static public int AdjustCosts(Pawn pawn, TerrainDef def, int cost, Map map, int index)
        {
            //Light factor
            if (factorLight && GameGlowAtFast(map, index) < 0.3f) cost += 2;
 
            //This will revert the terrain costs back to normal if...
            Faction faction = pawn?.Faction;
            if
            (
                faction != null &&
                (
                    (factorCarryingPawn && pawn.IsCarryingPawn()) || //Check carry rule
                    faction.HostileTo(Current.gameInt.worldInt.factionManager.ofPlayer) || //Is an enemy?
                    (factorBleeding && pawn.health.hediffSet.BleedRateTotal > 0.1f) //Is bleeding?
                )
            )
            {
                if (terrainCache.ContainsKey(def.shortHash)) cost += terrainCache[def.shortHash][1] * -1;
            }
            
            return cost < 0 ? 0 : cost;
        }

        public static float GameGlowAtFast(Map map, int index)
		{
			float daylight = 0f;
			if (map.roofGrid.roofGrid[index] != null)
			{
				daylight = map.skyManager.curSkyGlowInt;
				if (daylight == 1f)
				{
					return daylight;
				}
			}
			ColorInt color = map.glowGrid.glowGrid[index];
			if (color.a == 1) return 1f;

			return System.Math.Max(daylight, System.Math.Min(0.5f, (float)(color.r + color.g + color.b) / 3f / 255f * 3.6f));
		}
    }

    [HarmonyPatch (typeof(PathFinder), nameof(PathFinder.DetermineHeuristicStrength))]
    static class Patch_DetermineHeuristicStrength
    {
        static bool Prefix(ref float __result, Pawn pawn, IntVec3 start, LocalTargetInfo dest)
        {
            if (Custom_DistanceCurve == null || (pawn?.RaceProps.Animal ?? false)) return true;
            
            float lengthHorizontal = (start - dest.Cell).LengthHorizontal;
            __result = (float)System.Math.Round(Custom_DistanceCurve.Evaluate(lengthHorizontal));
            return false;
        }
    }
}