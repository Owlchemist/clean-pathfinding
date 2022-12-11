using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace CleanPathfinding
{
    [HarmonyPatch]
    static class Patch_DoorsExpanded
    {
        static MethodBase target;

        static bool Prepare()
        {
			ModContentPack mod = LoadedModManager.RunningMods.FirstOrDefault(m => m.Name == "Doors Expanded");
			Type type = mod?.assemblies.loadedAssemblies.FirstOrDefault(a => a.GetName().Name == "DoorsExpanded")?.GetType("DoorsExpanded.Building_DoorExpanded");
        	target = AccessTools.DeclaredMethod(type, "GetGizmos");

            if (target == null)
			{
                if (mod != null) Log.Warning("[Clean Pathfinding] Failed to integrate with Doors Expanded. Method not found.");
                return false;
            }

            DoorPathingUtility.usingDoorsExpanded = true;
            return true;
        }

        static MethodBase TargetMethod()
        {
            return target;
        }

		static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Building __instance)
        {
            if (!ModSettings_CleanPathfinding.doorPathing) foreach (var value in values) yield return value;
            else foreach (var item in DoorPathingUtility.GetGizmos(values, __instance)) yield return item;
		}
    }
}