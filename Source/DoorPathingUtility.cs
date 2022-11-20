using HarmonyLib;
using Verse;
using Verse.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using RimWorld;
using UnityEngine;
using Verse.Sound;
using static CleanPathfinding.DoorPathingUtility;
using static CleanPathfinding.ModSettings_CleanPathfinding;
 
namespace CleanPathfinding
{
	//Add gizmos to the doors
    [HarmonyPatch(typeof(Building_Door), nameof(Building_Door.GetGizmos))]
    static class Patch_Building_Door
    {
		static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Building_Door __instance)
        {
			return GetGizmos(values, __instance);
		}
	}

	//If a door is despawned, remove the offset
	[HarmonyPatch(typeof(Building_Door), nameof(Building_Door.DeSpawn))]
    static class Patch_Building_DoorDeSpawn
    {
		static void Prefix(Building_Door __instance)
        {
			if (doorPathing && compCache.TryGetValue(__instance.Map.uniqueID, out MapComponent_DoorPathing comp)) comp.doorCostCache[__instance.Map.cellIndices.CellToIndex(__instance.Position)] = 0;
		}
	}

	/*
	[HarmonyPatch(typeof(PathGrid), nameof(PathGrid.CalculatedCostAt))]
    static class Patch_PathGrid
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
			int doorCost = 45;
            bool ran = false;
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
	            if (codes[i].opcode == OpCodes.Ldc_I4_S && codes[i].OperandIs(doorCost))
	            {
		            codes.InsertRange(i + 3, new List<CodeInstruction>(){

                        new CodeInstruction(OpCodes.Ldloc_0),
                        new CodeInstruction(OpCodes.Ldarg_1), //intvec3 c
						new CodeInstruction(OpCodes.Ldarg_0),
						new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(PathGrid), nameof(PathGrid.map))),
                        new CodeInstruction(OpCodes.Call, typeof(DoorPathingUtility).GetMethod(nameof(DoorPathingUtility.AdjustCost))),
						new CodeInstruction(OpCodes.Add),
						new CodeInstruction(OpCodes.Stloc_0)
                    });
                    ran = true;
                    break;
                }
            }
            if (!ran) Log.Warning("[Clean Pathfinding] Doorpathing transpiler could not find target. There may be a mod conflict, or RimWorld updated?");
            return codes.AsEnumerable();
        }
	}
	*/
	static public class DoorPathingUtility
	{
		public enum DoorType { Normal = 1, Side, Emergency}
		public static Dictionary<int, MapComponent_DoorPathing> compCache = new Dictionary<int, MapComponent_DoorPathing>();

		public static IEnumerable<Gizmo> GetGizmos(IEnumerable<Gizmo> values, Building thing)
		{
			foreach (var value in values) yield return value;
			if (doorPathing)
			{
				if (compCache.TryGetValue(thing.Map.uniqueID, out MapComponent_DoorPathing doorPathingComp))
				{
					//Add a new registry entry if needed
					if (!doorPathingComp.doorRegistry.TryGetValue(thing.thingIDNumber, out DoorType doorType))
					{
						doorPathingComp.doorRegistry.Add(thing.thingIDNumber, DoorType.Normal);
						doorPathingComp.doorCostCache[thing.Map.cellIndices.CellToIndex(thing.Position)] = GetDoorCost(DoorType.Normal);
						doorType = DoorType.Normal;
					}
					
					yield return new Command_Action()
					{
						icon = ResourceBank.iconPriority,
						defaultDesc = "CleanPathfinding.Icon.DoorType.Desc".Translate(),
						defaultLabel = ("CleanPathfinding.Icon." + doorType.ToString()).Translate(),
						action = () => DoorPathingUtility.SwitchDoorType(doorPathingComp.doorRegistry, thing, doorType)
					};
				}
			}
		}

		//Handles the gizmo switching door types
		static public void SwitchDoorType(Dictionary<int, DoorType> doorRegistry, Building thing, DoorType doorType)
		{
			Map map = thing.Map;
			if (!ValidateDoor(map, thing.Position))
			{
				Messages.Message("CleanPathfinding.InvalidDoor".Translate(), MessageTypeDefOf.RejectInput, false);
				//Sanity reset if needed
				if (doorType != DoorType.Normal)
				{
					doorRegistry[thing.thingIDNumber] = DoorType.Normal;
					WriteToDoorCache(map, thing, DoorType.Normal);
				}
				return;
			}
			SoundDefOf.Click.PlayOneShotOnCamera(null);

			int length = Enum.GetValues(typeof(DoorType)).Length;
			doorType = doorType != DoorType.Emergency ? ++doorType : DoorType.Normal;
			doorRegistry[thing.thingIDNumber] = doorType;
			WriteToDoorCache(map, thing, doorType);
		}

		//A door is invalid for priority if it's the only door leading into a room. This method checks this.
		static bool ValidateDoor(Map map, IntVec3 c)
		{
			int numOfDoors = 0;
			//First, find the region the door is on
			Region doorRegion = c.GetRegion(map);

			//Now iterate through this region's links to look at adjacent rooms
			foreach (RegionLink regionLink in doorRegion.links)
			{
				//Fetch the room adjacent to the door
				Room room = regionLink.GetOtherRegion(doorRegion)?.Room;

				//Is it a room? This filters out the "wall regions" left and right of the door
				if (room != null && !room.TouchesMapEdge)
				{
					//Go through all the regions that make up the adjaent room
					foreach (var adjacentRegion in room.Regions)
					{
						//Try to look around the other walls of the room to find other doors
						foreach (RegionLink roomLink in adjacentRegion.links)
						{
							//Is this a door?
							var otherDoorRegion = roomLink.GetOtherRegion(adjacentRegion);
							if (otherDoorRegion.type == RegionType.Portal && otherDoorRegion.IsDoorway && otherDoorRegion.door.def.passability != Traversability.Impassable)
							{
								++numOfDoors;
							} 
						}
					}
				}
			}
			return numOfDoors > 1;
		}

		//Converts the doorType enum to the mod setting config
		public static int GetDoorCost(DoorType doorType)
		{
			if (doorType == DoorType.Normal) return 0;
			else if (doorType == DoorType.Side) return doorPathingSide;
			return doorPathingEmergency;
		}

		//All the doors' cost is written to cache. Called on map init and changing mod settings
		public static void RecalculateAllDoors(Map specificMap = null)
		{
			if (Current.ProgramState != ProgramState.Entry)
			{
				foreach (Map map in specificMap == null ? Find.Maps : Enumerable.Repeat(specificMap, 1))
				{
					if (compCache.TryGetValue(map.uniqueID, out MapComponent_DoorPathing comp))
					{
						var list = map.listerBuildings?.allBuildingsColonist?.Where(x => comp.doorRegistry.ContainsKey(x.thingIDNumber));
						foreach (Building thing in list ?? Enumerable.Empty<Building>()) WriteToDoorCache(map, thing, comp.doorRegistry[thing.thingIDNumber]);
					}
				}
			}
		}

		//Going through all cells a door occupies and writing the cost
		static void WriteToDoorCache(Map map, Thing thing, DoorType doorType)
		{
			foreach (var c in thing.OccupiedRect().Cells)
			{
				if (c.InBounds(map)) compCache[map.uniqueID].doorCostCache[map.cellIndices.CellToIndex(c)] = GetDoorCost(doorType);
			}
		}

		/*
		static public int AdjustCost(IntVec3 c, Map map)
		{
			Log.Message("is this running?");
			var doorRegistry = map?.GetComponent<MapComponent_DoorPathing>()?.doorRegistry;
			if (doorRegistry == null) return 0;

			doorRegistry.TryGetValue(c.GetEdifice(map).thingIDNumber, out DoorType doorType);
			if (doorType == DoorType.Side) {Log.Message("returning 45"); return 45;}
			if (doorType == DoorType.Emergency) {Log.Message("returning 1000"); return 1000;}
			Log.Message("returning 0");
			return 0;
		}
		*/
	}

	public class MapComponent_DoorPathing : MapComponent
	{
		public Dictionary<int, DoorType> doorRegistry;
		public int[] doorCostCache;

		public MapComponent_DoorPathing(Map map) : base(map)
		{
			if (!doorPathing) map.components.Remove(this);
		}

		public override void ExposeData()
		{
			if (doorPathing) Scribe_Collections.Look(ref this.doorRegistry, "doorRegistry");
		}
		
		public override void FinalizeInit()
		{
			if (doorRegistry == null) doorRegistry = new Dictionary<int, DoorType>();
			if (!compCache.ContainsKey(map.uniqueID)) compCache.Add(map.uniqueID, this);
			doorCostCache = new int[map.info.NumCells];
			RecalculateAllDoors(map);
		}
	}
}