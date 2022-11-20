using HarmonyLib;
using Verse.AI;
using static CleanPathfinding.ModSettings_CleanPathfinding;
 
namespace CleanPathfinding
{
    [HarmonyPatch (typeof(JobGiver_Wander), nameof(JobGiver_Wander.TryGiveJob))]
    static class Patch_JobGiver_Wander
    {
        static void Postfix(Job __result)
        {
			if (wanderTuning && __result?.def.index == RimWorld.JobDefOf.Wait_Wander.index) __result.expiryInterval += wanderDelay;
        }
    }
}