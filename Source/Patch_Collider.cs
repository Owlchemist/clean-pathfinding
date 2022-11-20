using HarmonyLib;
using Verse;
using Verse.AI;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using RimWorld;
 
namespace CleanPathfinding
{
	[HarmonyPatch (typeof(PawnUtility), nameof(PawnUtility.ShouldCollideWithPawns))]
    static class Patch_ShouldCollideWithPawns
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            if (!ModSettings_CleanPathfinding.optimizeCollider || instructions.Count() != 16) {
                foreach (var code in instructions) yield return code;
                if (ModSettings_CleanPathfinding.optimizeCollider) Log.Warning("[Clean Pathfinding] Could not apply collider optimization patch. There may be a mod conflict, or RimWorld updated?");
                yield break;
            }

            var label = generator.DefineLabel();
            var endLabel = new CodeInstruction(OpCodes.Ldc_I4_0);
            endLabel.labels.Add(label);

            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn), nameof(Pawn.mindState)));
            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn_MindState), nameof(Pawn_MindState.anyCloseHostilesRecently)));
            yield return new CodeInstruction(OpCodes.Brfalse_S, label);
            
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn), nameof(Pawn.health)));
            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.healthState)));
            yield return new CodeInstruction(OpCodes.Ldc_I4_1);
            yield return new CodeInstruction(OpCodes.Beq_S, label);
            
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn), nameof(Pawn.health)));
            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.healthState)));
            yield return new CodeInstruction(OpCodes.Ldc_I4_0);
            yield return new CodeInstruction(OpCodes.Cgt_Un);
            yield return new CodeInstruction(OpCodes.Ret);
            
            yield return endLabel;
            yield return new CodeInstruction(OpCodes.Ret);
        }
	}
}