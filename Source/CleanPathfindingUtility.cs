using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using static CleanPathfinding.CleanPathfindingUtility;
using static CleanPathfinding.ModSettings_CleanPathfinding;
 
namespace CleanPathfinding
{
	[HarmonyPatch(typeof(PathFinder), nameof(PathFinder.FindPath), new Type[] { 
        typeof(IntVec3), 
        typeof(LocalTargetInfo), 
        typeof(TraverseParms), 
        typeof(PathEndMode), 
        typeof(PathFinderCostTuning) })]
    static class Patch_PathFinder
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool ran = false;
            int offset = -1;
			bool searchForObjects = false;
			int objectsFound = 0;
			object[] objects = new object[3];
            foreach (var code in instructions)
            {
                yield return code;
				if (!searchForObjects && code.opcode == OpCodes.Ldfld && code.OperandIs(AccessTools.Field(typeof(TerrainDef), nameof(TerrainDef.extraDraftedPerceivedPathCost))))
                {
                    searchForObjects = true;
                    continue;
                }

				//Record which local variables extraNonDraftedPerceivedPathCost is using, instead of blindly pulling from the local array ourselves which may jumble
				if (searchForObjects && objectsFound < 3 && code.opcode == OpCodes.Ldloc_S)
                {
					objects[objectsFound++] = code.operand;
					//As of 12/5, object 0 should be 48, object 1 should be 12, and object 2 should be 45
				}

                if (offset == -1 && code.opcode == OpCodes.Ldfld && code.OperandIs(AccessTools.Field(typeof(TerrainDef), nameof(TerrainDef.extraNonDraftedPerceivedPathCost))))
                {
                    offset = 0;
                    continue;
                }
                if (offset > -1 && ++offset == 2)
                {
					//code.operand
                    yield return new CodeInstruction(OpCodes.Ldloc_0);
                    yield return new CodeInstruction(OpCodes.Ldloc_S, objects[1]); //topGrid
                    yield return new CodeInstruction(OpCodes.Ldloc_S, objects[2]); //TerrainDef within the grid
                    yield return new CodeInstruction(OpCodes.Ldelem_Ref);
                    yield return new CodeInstruction(OpCodes.Ldloc_S, objects[0]); //Pathcost total
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(PathFinder), nameof(PathFinder.map)));
                    yield return new CodeInstruction(OpCodes.Ldloc_S, objects[2]); //cell location
                    yield return new CodeInstruction(OpCodes.Call, typeof(CleanPathfindingUtility).GetMethod(nameof(CleanPathfindingUtility.AdjustCosts)));
                    yield return new CodeInstruction(OpCodes.Stloc_S, objects[0]);

                    ran = true;
                }
            }
            
            if (!ran) Log.Warning("[Clean Pathfinding] Transpiler could not find target. There may be a mod conflict, or RimWorld updated?");
        }
    }

    [HarmonyPatch (typeof(PathFinder), nameof(PathFinder.DetermineHeuristicStrength))]
    static class Patch_DetermineHeuristicStrength
    {
        static bool Prefix(ref float __result, Pawn pawn, IntVec3 start, LocalTargetInfo dest)
        {
            if (extraRange == 0 || pawn == null || pawn.def.race.intelligence < Intelligence.Humanlike) return true;
            
            float lengthHorizontal = (start - dest.Cell).LengthHorizontal;
            __result = (float)System.Math.Round(Custom_DistanceCurve.Evaluate(lengthHorizontal));
            return false;
        }
    }

    public static class CleanPathfindingUtility
	{	
		public static Dictionary<ushort, int[]> terrainCache = new Dictionary<ushort, int[]>();
		public static SimpleCurve Custom_DistanceCurve;
		static List<string> report = new List<string>();
		public static int cachedMapID = -1;
		public static MapComponent_DoorPathing cachedComp;

		public static void UpdatePathCosts()
		{
			try
			{
				//Reset the cache
				report.Clear();
				foreach (var item in terrainCache) item.Value[1] = 0;

				foreach (var terrainDef in DefDatabase<TerrainDef>.AllDefsListForReading)
				{
					ushort index = terrainDef.index;
					//Reset to original value
					if (terrainCache.ContainsKey(index)) terrainDef.extraNonDraftedPerceivedPathCost = terrainCache[index][0];
					
					//Avoid filth
					if (bias > 0 && terrainDef.generatedFilth != null)
					{
						terrainDef.extraNonDraftedPerceivedPathCost += bias; 
						terrainCache[index][1] = bias;
					}

					//Clean but natural terrain bias
					if (naturalBias > 0 && terrainDef.generatedFilth == null && (terrainDef.defName.Contains("_Rough")))
					{
						terrainDef.extraNonDraftedPerceivedPathCost += naturalBias; 
						terrainCache[index][1] += naturalBias;
					}

					//Attraction to roads
					if (roadBias > 0 && (terrainDef.tags?.Contains("CleanPath") ?? false))
					{
						terrainDef.extraNonDraftedPerceivedPathCost -= roadBias;
						terrainCache[index][1] -= roadBias;
					}

					//Debug
					if (logging && Prefs.DevMode)
					{
						report.Add(terrainDef.defName + ": " + terrainDef.extraNonDraftedPerceivedPathCost);
					}
				}

				//Debug print
				if (logging && Prefs.DevMode)
				{
					report.Sort();
					Log.Message("[Clean Pathfinding] Terrain report:\n" + string.Join("\n - ", report));
				}

				//Reset the extra pathfinding range curve
				if (extraRange == 0) Custom_DistanceCurve = null;
				else
				{
					Custom_DistanceCurve = new SimpleCurve
					{
						{
							new CurvePoint(40f + extraRange, 1f),
							true
						},
						{
							new CurvePoint(120f + extraRange, 2.8f),
							true
					}};
				}

				//If playing, update the pathfinders now
				if (Current.ProgramState == ProgramState.Playing) Find.Maps.ForEach(x => x.pathing.RecalculateAllPerceivedPathCosts());	 
			}
			catch (System.Exception ex)
			{                
				Log.Error("[Clean Pathfinding] Error processing settings, skipping...\n" + ex);
			}
		}

		static int loggedOnTick = 0;
		static int calls = 0;

		static public int AdjustCosts(Pawn pawn, TerrainDef def, int cost, Map map, int index)
        {
            //Revert path costs based on rules, also factor light
            Faction faction = pawn?.factionInt;
            if (faction != null && pawn.def.race.intelligence == Intelligence.Humanlike)
            {
                bool revert = false;
                if (!faction.def.isPlayer && faction.HostileTo(Current.gameInt.worldInt.factionManager.ofPlayer)) revert = true;
                //Light factor and doorpathing
                else
                {
                    if (factorLight && GameGlowAtFast(map, index) < 0.3f) cost += 2;
                    if (doorPathing)
					{
						int doorCost = 0;
						if (cachedMapID == map.uniqueID) doorCost = cachedComp.doorCostGrid[index];
						else if (DoorPathingUtility.compCache.TryGetValue(map.uniqueID, out cachedComp))
						{
							cachedMapID = map.uniqueID;
							doorCost = cachedComp.doorCostGrid[index];
						}
						if (doorCost == -45) return -45;
						else cost += doorCost;
					}
                }

                if (!revert && ((factorCarryingPawn && pawn.carryTracker?.CarriedThing?.def.category == ThingCategory.Pawn) || (factorBleeding && pawn.health.hediffSet.cachedBleedRate > 0.1f))) revert = true;
                
                //Revert if needed
                if (revert && terrainCache.TryGetValue(def.index, out int[] thisTerrain)) cost += thisTerrain[1] * -1;
				
				//Logging and debugging stuff
				if (logging && Verse.Prefs.DevMode)
				{
					++calls;
					if (Find.TickManager.ticksGameInt != loggedOnTick)
					{
						loggedOnTick = Find.TickManager.ticksGameInt;
						if (calls != 0) Log.Message("[Clean Pathfinding] Calls last pathfinding: " + calls.ToString());
						calls = 0;
					}
					if (cost < 0) cost = 0;
					map.debugDrawer.FlashCell(map.cellIndices.IndexToCell(index), cost , cost.ToString());
				}

                return cost < 0 ? 0 : cost;
            }
            
            return cost;
        }

        public static float GameGlowAtFast(Map map, int index)
		{
			float daylight = 0f;
			if (map.roofGrid.roofGrid[index] != null)
			{
				daylight = map.skyManager.curSkyGlowInt;
				if (daylight == 1f) return daylight;
			}
			ColorInt color = map.glowGrid.glowGrid[index];
			return color.a == 1 ? 1f :
            //n / 3f / 255f * 3.6f pre-computed, since I guess the assembler doesn't optimize this
			System.Math.Max(daylight, System.Math.Min(0.5f, (float)(color.r + color.g + color.b) * 0.0047058823529412f));
		}
	}
}