using HarmonyLib;
using Verse.AI;
using static CleanPathfinding.ModSettings_CleanPathfinding;
using static CleanPathfinding.Mod_CleanPathfinding;
 
namespace CleanPathfinding
{
    [HarmonyPatch (typeof(JobGiver_Wander), nameof(JobGiver_Wander.TryGiveJob))]
    static class Patch_JobGiver_Wander
    {
        static bool Prepare()
        {
            if (!patchLedger.ContainsKey(nameof(Patch_JobGiver_Wander))) patchLedger.Add(nameof(Patch_JobGiver_Wander), wanderTuning);
            return wanderTuning;
        }
        public static void Postfix(Job __result)
        {
			if (__result?.def.index == RimWorld.JobDefOf.Wait_Wander.index) __result.expiryInterval += wanderDelay;
        }
    }
}