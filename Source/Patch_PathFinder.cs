using HarmonyLib;
using Verse;
using Verse.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using RimWorld;
using UnityEngine;
using static CleanPathfinding.Mod_CleanPathfinding;
using static CleanPathfinding.ModSettings_CleanPathfinding;
 
namespace CleanPathfinding
{
    [HarmonyPatch (typeof(DefGenerator), nameof(DefGenerator.GenerateImpliedDefs_PostResolve))]
    static class Patch_DefGenerator
    {
        static void Postfix()
        {
            DefDatabase<TerrainDef>.AllDefs.Where(x => x.generatedFilth != null || (x.tags != null && x.tags.Contains("Road"))).ToList().ForEach(y => 
            {
                terrainCache.Add(y.GetHashCode(),new int[2]{ y.extraNonDraftedPerceivedPathCost, 0 });
                //Log.Message(y.defName + " is " + y.GetHashCode().ToString());
            });
            Mod_CleanPathfinding.UpdatePathCosts();
        }
    }

    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.FindPath), new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode), typeof(PathFinderCostTuning) })]
    static class Patch_PathFinder
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var mapInfo = AccessTools.Field(typeof(PathFinder), "map");
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
                        new CodeInstruction(OpCodes.Ldloc_S, 43), //TerrainDef within the grid
                        new CodeInstruction(OpCodes.Ldelem_Ref),
                        new CodeInstruction(OpCodes.Ldloc_S, 46), //Pathcost total
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldfld, mapInfo),
                        new CodeInstruction(OpCodes.Ldloc_S, 43), //cell location
                        new CodeInstruction(OpCodes.Call, typeof(Patch_PathFinder).GetMethod(nameof(Patch_PathFinder.AdjustCosts))),
                        new CodeInstruction(OpCodes.Stloc_S, 46)
                    });
                    ran = true;
                    break;
                }
            }
            if (!ran) Log.Warning("[Clean Pathfinding] Transpiler could not find target. There may be a mod conflict, or RimWorld updated?");
            //else if (ran && Prefs.DevMode) Log.Message("[Clean Pathfinding] Loading complete");
            return codes.AsEnumerable();
        }

        static public int AdjustCosts(Pawn pawn, TerrainDef def, int cost, Map map, int index)
        {
            //Light factor
            if (factorLight)
            {
                IntVec3 intVec = map.cellIndices.IndexToCell(index); //For some reason I can't seem to get the transpiler to pass along the coordinate to skip refetching it. Will research some more later.
                var num = map.glowGrid.GameGlowAt(intVec, false);
                if (num < 0.3f) cost += 2;
            }

            //Check other factors: carry pawn check, hostile check, bleeding check
            if (pawn?.Faction != null
                && ((factorCarryingPawn && pawn.IsCarryingPawn()) 
                || pawn.Faction.HostileTo(Faction.OfPlayer) 
                || (factorBleeding && pawn.health.hediffSet.BleedRateTotal > 0.1f)) 
                && terrainCache.ContainsKey(def.GetHashCode()))
            {
                cost += terrainCache[def.GetHashCode()][1] * -1;
            }
            
            //This is to check if the road bias went too low for some reason
            if (cost < 0) cost = 0;
            return cost;
        }
    }

    [HarmonyPatch (typeof(PathFinder), "DetermineHeuristicStrength")]
    static class Patch_DetermineHeuristicStrength
    {
        static bool Prefix(ref float __result, Pawn pawn, IntVec3 start, LocalTargetInfo dest)
        {
            if (Custom_DistanceCurve == null || (pawn != null && pawn.RaceProps.Animal)) return true;
            
            float lengthHorizontal = (start - dest.Cell).LengthHorizontal;
            __result = (float)Mathf.RoundToInt(Custom_DistanceCurve.Evaluate(lengthHorizontal));
            return false;
        }
    }
}