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
                Mod_CleanPathfinding.terrainCache.Add(y.GetHashCode(),new int[2]{ y.extraNonDraftedPerceivedPathCost, 0 });
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
            var rangeInfo = AccessTools.Field(typeof(TerrainDef), nameof(TerrainDef.extraNonDraftedPerceivedPathCost));
                
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldfld && codes[i].OperandIs(rangeInfo))
                {
                    codes.InsertRange(i + 3, new List<CodeInstruction>(){

                        new CodeInstruction(OpCodes.Ldloc_S, 46), //Pathcost total
                        new CodeInstruction(OpCodes.Ldloc_0),
                        new CodeInstruction(OpCodes.Ldloc_S, 12), //topGrid
                        new CodeInstruction(OpCodes.Ldloc_S, 43), //TerrainDef within the grid
                        new CodeInstruction(OpCodes.Ldelem_Ref),
                        new CodeInstruction(OpCodes.Call, typeof(Patch_PathFinder).GetMethod(nameof(Patch_PathFinder.AdjustCostForHostiles))),
                        new CodeInstruction(OpCodes.Add),
                        new CodeInstruction(OpCodes.Stloc_S, 46)
                    });
                    break;
                }
            }
            return codes.AsEnumerable();
        }

        static public int AdjustCostForHostiles(Pawn pawn, TerrainDef def)
        {
            return (pawn != null && pawn.Faction != null && pawn.Faction.HostileTo(Faction.OfPlayer) && terrainCache.ContainsKey(def.GetHashCode())) ? (terrainCache[def.GetHashCode()][1] * -1) : 0;
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