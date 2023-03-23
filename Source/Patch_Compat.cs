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
        	target = AccessTools.DeclaredMethod(AccessTools.TypeByName("DoorsExpanded.Building_DoorExpanded"), "GetGizmos");
            return target != null;
        }

        static MethodBase TargetMethod()
        {
            DoorPathingUtility.usingDoorsExpanded = true;
            return target;
        }

		static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Building __instance)
        {
            if (!ModSettings_CleanPathfinding.doorPathing) foreach (var value in values) yield return value;
            else foreach (var item in DoorPathingUtility.GetGizmos(values, __instance)) yield return item;
		}
    }
}