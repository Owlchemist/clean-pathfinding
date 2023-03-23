using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using static CleanPathfinding.ModSettings_CleanPathfinding;
 
namespace CleanPathfinding
{
	#region Harmony
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
            int offset = -1, objectsFound = 0;
			bool ran = false, searchForObjects = false, thresholdReplaced = false;

			var method_regionModeThreshold = AccessTools.Field(typeof(ModSettings_CleanPathfinding), nameof(ModSettings_CleanPathfinding.regionModeThreshold));
			var field_extraDraftedPerceivedPathCost = AccessTools.Field(typeof(TerrainDef), nameof(TerrainDef.extraDraftedPerceivedPathCost));
			var field_extraNonDraftedPerceivedPathCost = AccessTools.Field(typeof(TerrainDef), nameof(TerrainDef.extraNonDraftedPerceivedPathCost));

			object[] objects = new object[3];
            foreach (var code in instructions)
            {
				//Replace region-mode pathing threshold
				if (!thresholdReplaced && code.opcode == OpCodes.Ldc_I4 && code.OperandIs(100000))
                {
					code.opcode = OpCodes.Ldsfld;
					code.operand = method_regionModeThreshold;
                }
                yield return code;
				if (!searchForObjects && code.opcode == OpCodes.Ldfld && code.OperandIs(field_extraDraftedPerceivedPathCost))
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

                if (offset == -1 && code.opcode == OpCodes.Ldfld && code.OperandIs(field_extraNonDraftedPerceivedPathCost))
                {
                    offset = 0;
                    continue;
                }
                if (offset > -1 && ++offset == 2)
                {
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
    
	#endregion

    public static class CleanPathfindingUtility
	{	
		public static Dictionary<ushort, int> terrainCache = new Dictionary<ushort, int>(), terrainCacheOriginalValues = new Dictionary<ushort, int>();
		public static SimpleCurve Custom_DistanceCurve;
		public static MapComponent_DoorPathing cachedComp; //the last map component used by the door cost adjustments. It will be reused if the map hash is the same
		static bool lastFactionHostileCache, lastPawnReversionCache;
		public static int cachedMapID = -1, lastFactionID = -1;
		static int loggedOnTick, calls, lastTerrainCacheCost, lastPawnID;
		static ushort lastTerrainDefID;

		public static void UpdatePathCosts()
		{
			try
			{
				//Reset the cache
				List<string> report = new List<string>();
				foreach (ushort key in terrainCache.Keys.ToList())
				{
					if (terrainCacheOriginalValues.TryGetValue(key, out int originalValue)) terrainCache[key] = originalValue;
					else terrainCache[key] = 0;
				}

				var list = DefDatabase<TerrainDef>.AllDefsListForReading;
				var length = list.Count;
				for (int i = 0; i < length; i++)
				{
					TerrainDef terrainDef = list[i];
				
					ushort index = terrainDef.shortHash;
					//Reset to original value
					if (terrainCache.ContainsKey(index)) terrainDef.extraNonDraftedPerceivedPathCost = terrainCacheOriginalValues[index];
					else continue;
					
					//Attraction to roads
					if (roadBias > 0 && (terrainDef.tags?.Contains("CleanPath") ?? false))
					{
						terrainDef.extraNonDraftedPerceivedPathCost -= roadBias;
						terrainCache[index] += roadBias;
					}
					else
					{
						//Avoid filth
						if (bias != 0 && terrainDef.generatedFilth != null)
						{
							terrainDef.extraNonDraftedPerceivedPathCost += bias; 
							terrainCache[index] -= bias;
						}

						//Clean but natural terrain bias
						if (naturalBias > 0 && terrainDef.generatedFilth == null && (terrainDef.defName.Contains("_Rough")))
						{
							terrainDef.extraNonDraftedPerceivedPathCost += naturalBias; 
							terrainCache[index] -= naturalBias;
						}
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
				Custom_DistanceCurve = new SimpleCurve
				{
					{ new CurvePoint(40f + heuristicAdjuster, 1f), true },
					{ new CurvePoint(120f + (heuristicAdjuster * 3), 3f), true }
				};

				//If playing, update the pathfinders now
				if (Current.ProgramState == ProgramState.Playing) foreach (Map map in Find.Maps) map.pathing.RecalculateAllPerceivedPathCosts();
			}
			catch (System.Exception ex)
			{                
				Log.Error("[Clean Pathfinding] Error processing settings, skipping...\n" + ex);
			}
		}
		static public int AdjustCosts(Pawn pawn, TerrainDef def, int cost, Map map, int index)
        {
			if (pawn == null) goto skipAdjustment;

			//Do not do cost adjustments if...
			bool revert = lastPawnReversionCache;

			//Is not this the last pawn we checked?
			if (pawn.thingIDNumber != lastPawnID)
			{
				lastPawnID = pawn.thingIDNumber;
				var faction = pawn.Faction;

				revert = ((faction == null || pawn.def.race.intelligence == Intelligence.Animal) || // Animal or other entity?
				(!faction.def.isPlayer && IsHostileFast(faction)) || //They are hostile
				(factorCarryingPawn && pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null && pawn.carryTracker.CarriedThing.def.category == ThingCategory.Pawn) ||  //They are carrying someone
				(factorBleeding && pawn.health.hediffSet.cachedBleedRate > 0.1f)); //They are bleeding

				lastPawnReversionCache = revert;
			}

			if (!revert)
			{
				//Factor in door pathing
				if (doorPathing)
				{
					int doorCost = 0;
					if (cachedMapID == map.uniqueID) doorCost = cachedComp.doorCostGrid[index];
					else if (DoorPathingUtility.compCache.TryGetValue(map.uniqueID, out cachedComp))
					{
						cachedMapID = map.uniqueID;
						doorCost = cachedComp.doorCostGrid[index];
					}
					if (doorCost < 0) goto skipAdjustment;
					cost += doorCost;
				}
				//...And then light pathing
				if (factorLight && GameGlowAtFast(map, index) < 0.3f) cost += darknessPenalty;
			}
			//Revert if needed, check if cache is available
			else if (def.shortHash == lastTerrainDefID) cost += lastTerrainCacheCost;
			//If not, use and set...
			else
			{
				lastTerrainDefID = def.shortHash;
				if (terrainCache.TryGetValue(def.shortHash, out lastTerrainCacheCost))
				{
					//Double-revert back to 0 if this is a clean path (value returned as greater than 0) and this is our faction, meaning it's probably a hauling animal
					if (lastTerrainCacheCost > 0 && pawn.factionInt != null && pawn.factionInt.def.isPlayer) lastTerrainCacheCost = 0;
					cost += lastTerrainCacheCost;
				}
				else lastTerrainCacheCost = 0; //Record any terrain defs that are not modified to avoid looking them up again
			}
			
			//Logging and debugging stuff
			if (logging && Verse.Prefs.DevMode)
			{
				++calls;
				if (Current.gameInt.tickManager.ticksGameInt != loggedOnTick)
				{
					loggedOnTick = Current.gameInt.tickManager.ticksGameInt;
					if (calls != 0) Log.Message("[Clean Pathfinding] Calls last pathfinding: " + calls);
					calls = 0;
				}
				if (cost < 0) cost = 0;
				var cell = map.cellIndices.IndexToCell(index);
				if (!map.debugDrawer.debugCells.Any(x => x.c == cell)) map.debugDrawer.FlashCell(cell, cost, cost.ToString());
			}
			skipAdjustment:
			if (cost < 0) return 0;
			return cost;

			#region embedded methods
			bool IsHostileFast(Faction faction)
			{
				//Check and set cache
				if (Current.gameInt.tickManager.ticksGameInt % 600 == 0) lastFactionID = -1; // Reset every 10th second
				if (faction.loadID == lastFactionID) return lastFactionHostileCache;
				else lastFactionID = faction.loadID;

				//Look through their relationships table and look up the player faction, then record to cache
				var relations = faction.relations;
				for (int i = relations.Count; i-- > 0;)
				{
					var tmp = relations[i];
					if (tmp.other == Current.gameInt.worldInt.factionManager.ofPlayer)
					{
						lastFactionHostileCache = tmp.kind == FactionRelationKind.Hostile;
						break;
					}
				}
				return lastFactionHostileCache;
			}

			float GameGlowAtFast(Map map, int index)
			{
				float daylight = 0f;
				//If there's no roof, they're outside, so factory the daylight
				if (map.roofGrid.roofGrid[index] == null)
				{
					daylight = map.skyManager.curSkyGlowInt;
					if (daylight == 1f) return 1f;
				}
				ColorInt color = map.glowGrid.glowGrid[index];
				if (color.a == 1) return 1;

				return (float)(color.r + color.g + color.b) * 0.0047058823529412f; //n / 3f / 255f * 3.6f pre-computed, since I guess the assembler doesn't optimize this
			}

			#endregion
        }
	}
}