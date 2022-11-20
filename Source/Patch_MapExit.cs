using HarmonyLib;
using Verse;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using RimWorld;
 
namespace CleanPathfinding
{
	[HarmonyPatch (typeof(RCellFinder), nameof(RCellFinder.TryFindBestExitSpot))]
    static class Patch_TryFindBestExitSpot
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!ModSettings_CleanPathfinding.exitTuning)
            {
                foreach (var code in instructions) yield return code;
                yield break;
            }

            bool ran = false;
            foreach (var code in instructions)
            {
                yield return code;
                if (code.opcode == OpCodes.Ldc_I4_S)
                {
                    yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(ModSettings_CleanPathfinding), nameof(ModSettings_CleanPathfinding.exitRange)));
                    yield return new CodeInstruction(OpCodes.Add);
                    ran = true;
                }
            }
            
            if (!ran) Log.Warning("[Clean Pathfinding] Transpiler could not find target for exit finding patch. There may be a mod conflict, or RimWorld updated?");
        }
	}
}