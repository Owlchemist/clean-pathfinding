using HarmonyLib;
using Verse;
using Verse.AI;
using System;
using System.Collections.Generic;
//using System.Linq;
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
		static bool Prepare()
		{
			if (!Mod_CleanPathfinding.patchLedger.ContainsKey(nameof(Patch_Building_Door))) Mod_CleanPathfinding.patchLedger.Add(nameof(Patch_Building_Door), doorPathing);
            return doorPathing;
		}
		public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Building_Door __instance)
        {
			return GetGizmos(values, __instance);
		}
	}

	//If a door is despawned, remove the offset
	[HarmonyPatch(typeof(Building_Door), nameof(Building_Door.DeSpawn))]
    static class Patch_Building_DoorDeSpawn
    {
		static bool Prepare()
		{
			return doorPathing;
		}
		public static void Prefix(Building_Door __instance)
        {
			if (compCache.TryGetValue(__instance.Map.uniqueID, out MapComponent_DoorPathing comp)) comp.doorCostCache[__instance.Map.cellIndices.CellToIndex(__instance.Position)] = 0;
		}
	}

	//If a door is despawned, remove the offset
	[HarmonyPatch(typeof(Room), nameof(Room.Notify_RoomShapeChanged))]
    static class Patch_Notify_RoomShapeChanged
    {
		static bool Prepare()
		{
			return doorPathing;
		}
		public static void Postfix(Room __instance)
        {
			if (compCache.TryGetValue(__instance.Map?.uniqueID ?? -1, out MapComponent_DoorPathing mapComp)) 
			{
				foreach (var cell in __instance.Cells)
				{
					mapComp.ValidateRoomDoors(cell, true);
					break;
				}
			}
		}
	}

	static public class DoorPathingUtility
	{
		public enum DoorType { Normal = 1, Side, Emergency}
		public static Dictionary<int, MapComponent_DoorPathing> compCache = new Dictionary<int, MapComponent_DoorPathing>();

		public static IEnumerable<Gizmo> GetGizmos(IEnumerable<Gizmo> values, Building thing)
		{
			foreach (var value in values) yield return value;
			if (thing.def.passability != Traversability.Impassable)
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
						action = () => doorPathingComp.SwitchDoorType(thing, doorType)
					};
				}
			}
		}

		//Converts the doorType enum to the mod setting config
		public static int GetDoorCost(DoorType doorType)
		{
			if (doorType == DoorType.Normal) return 0;
			else if (doorType == DoorType.Side) return doorPathingSide;
			return doorPathingEmergency;
		}

		public static void UpdateAllDoorsOnAllMaps()
		{
			Dialog_MessageBox reloadGameMessage = new Dialog_MessageBox("CleanPathfinding.ReloadRequired".Translate(), null, null, null, null, "CleanPathfinding.ReloadHeader".Translate(), true, null, null, WindowLayer.Dialog);

			if (Current.ProgramState == ProgramState.Playing)
			{
				foreach (var map in Find.Maps)
				{
					if (compCache.TryGetValue(map.uniqueID, out MapComponent_DoorPathing mapComp)) mapComp.RecalculateAllDoors();
					else
					{
						if (!Find.WindowStack.IsOpen(reloadGameMessage)) Find.WindowStack.Add(reloadGameMessage);
					}
				}
			}
		}
	}

	public class MapComponent_DoorPathing : MapComponent
	{
		public Dictionary<int, DoorType> doorRegistry = new Dictionary<int, DoorType>();
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
			if (!compCache.ContainsKey(map.uniqueID)) compCache.Add(map.uniqueID, this);
			doorCostCache = new int[map.info.NumCells];
			RecalculateAllDoors();
		}

		//All the doors' cost is written to cache. Called on map init and changing mod settings
		public void RecalculateAllDoors()
		{
			try
			{
				List<Building> list = map.listerBuildings.allBuildingsColonist;
				var length = list.Count;
				for (int i = 0; i < length; i++)
				{
					var building = list[i];
					if (doorRegistry.ContainsKey(building.thingIDNumber)) WriteToDoorCache(map, building, doorRegistry[building.thingIDNumber]);
				}
			}
			catch (System.Exception ex)
			{                
				Log.Error("[Clean Pathfinding] Error processing door recalculation, skipping...\n" + ex);
			}
		}

		//Handles the gizmo switching door types
		public void SwitchDoorType(Building thing, DoorType doorType)
		{
			if (!ValidateRoomDoors(thing.Position))
			{
				Messages.Message("CleanPathfinding.InvalidDoor".Translate(), MessageTypeDefOf.RejectInput, false);
				return;
			}
			SoundDefOf.Click.PlayOneShotOnCamera(null);

			int length = Enum.GetValues(typeof(DoorType)).Length;
			doorType = doorType != DoorType.Emergency ? ++doorType : DoorType.Normal;
			doorRegistry[thing.thingIDNumber] = doorType;
			WriteToDoorCache(map, thing, doorType);
		}

		//Going through all cells a door occupies and writing the cost
		void WriteToDoorCache(Map map, Thing thing, DoorType doorType)
		{
			foreach (var c in thing.OccupiedRect().Cells)
			{
				if (c.InBounds(map)) doorCostCache[map.cellIndices.CellToIndex(c)] = GetDoorCost(doorType);
			}
		}

		//A door is invalid for priority if it's the only door leading into a room. This method checks this.
		public bool ValidateRoomDoors(IntVec3 c, bool roomUpdate = false)
		{
			//First, find the region the door is on
			if (roomUpdate)
			{
				var tmp = c.GetRegion(map);
				if (tmp?.door != null) c = tmp.door.positionInt;
				else return true;
			}
			
			if (logging && Prefs.DevMode) map.debugDrawer.FlashCell(c, text: "REF");
			Region doorRegion = c.GetRegion(map);
			if (doorRegion == null) return true;

			//Prepare list of doors to update
			List<Building> doorsInRoom = new List<Building>();
			
			//Now iterate through this region's links to look at adjacent rooms
			foreach (RegionLink regionLink in doorRegion.links)
			{
				//Fetch the room adjacent to the door
				Room room = regionLink.GetOtherRegion(doorRegion)?.Room;

				//Is it a room? This filters out the "wall regions" left and right of the door
				if (room != null && !room.TouchesMapEdge)
				{
					if (logging && Verse.Prefs.DevMode) map.debugDrawer.FlashCell(room.FirstRegion.AnyCell, text: "\nROOM");
					//Go through all the regions that make up the adjaent room
					foreach (var adjacentRegion in room.Regions)
					{
						//Try to look around the other walls of the room to find other doors
						foreach (RegionLink roomLink in adjacentRegion.links)
						{
							//Is this a door?
							Region otherDoorRegion = roomLink.GetOtherRegion(adjacentRegion);
							if (otherDoorRegion.type == RegionType.Portal && otherDoorRegion.IsDoorway && otherDoorRegion.door.def.passability != Traversability.Impassable)
							{
								if (logging && Prefs.DevMode) map.debugDrawer.FlashCell(otherDoorRegion.door.positionInt, text: "\n\nDOOR");
								if (!doorsInRoom.Contains(otherDoorRegion.door)) doorsInRoom.Add(otherDoorRegion.door);
							} 
						}
					}
				}
			}
			//Sanity check doors
			if (logging && Prefs.DevMode) Log.Message("[Clean Pathfinding] Doors in new room layout: " + doorsInRoom.Count.ToString());
			if (doorsInRoom.Count == 1)
			{
				foreach (var item in doorsInRoom)
				{
					if (doorRegistry.ContainsKey(item.thingIDNumber))
					{
						doorRegistry[item.thingIDNumber] = DoorType.Normal;
						WriteToDoorCache(map, item, DoorType.Normal);
					} 
				}
			}
			return doorsInRoom.Count > 1;
		}
	}
}