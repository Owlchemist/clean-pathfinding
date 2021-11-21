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
            var field = AccessTools.Field(typeof(ModSettings_CleanPathfinding), nameof(ModSettings_CleanPathfinding.exitRange));
            bool ran = false;
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
	            if (codes[i].opcode == OpCodes.Ldc_I4_S)
	            {
		            codes.InsertRange(i + 1, new List<CodeInstruction>(){

                        new CodeInstruction(OpCodes.Ldsfld, field),
                        new CodeInstruction(OpCodes.Add)
                    });
                    ran = true;
                    break;
                }
            }
            if (!ran) Log.Warning("[Clean Pathfinding] Transpiler could not find target for exit range patch. There may be a mod conflict, or RimWorld updated?");
            return codes.AsEnumerable();
        }
	}
}