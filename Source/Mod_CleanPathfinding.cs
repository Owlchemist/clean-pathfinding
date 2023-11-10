
using Verse;
using Verse.AI;
using RimWorld;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using static CleanPathfinding.ModSettings_CleanPathfinding;
 
namespace CleanPathfinding
{
	[StaticConstructorOnStartup]
	public static class Setup
	{
        static public bool safetyNeeded = true;
		static Setup()
        {
			var report = new List<string>();
			var list = DefDatabase<TerrainDef>.AllDefsListForReading;
			for (int i = list.Count; i-- > 0;)
			{
				TerrainDef terrainDef = list[i];

				if (terrainDef.destroyEffectWater != null)
				{
					if (terrainDef.tags == null) terrainDef.tags = new List<string>();
					terrainDef.tags.Add("CleanPath");
				}

				bool isClean = terrainDef.tags?.Contains("CleanPath") ?? false;

				if
				(
					terrainDef.generatedFilth != null || //Generates filth?
					isClean || //Is a road?
					(terrainDef.generatedFilth == null && terrainDef.defName.Contains("_Rough") ) //Is clean but avoided regardless?
				)
				{
					CleanPathfindingUtility.terrainCacheOriginalValues.Add(terrainDef.shortHash, terrainDef.extraNonDraftedPerceivedPathCost);
					CleanPathfindingUtility.terrainCache.Add(terrainDef.shortHash, terrainDef.extraNonDraftedPerceivedPathCost);

					if (isClean) report.Add(terrainDef.label);
				} 
			}

			SafetyCheck();
            
            CleanPathfindingUtility.UpdatePathCosts();
			if (Prefs.DevMode) Log.Message("[Clean Pathfinding] The following terrains apply to road attraction:\n - " + string.Join("\n - ", report));
		}

		static void SafetyCheck()
		{
			var list = LoadedModManager.RunningModsListForReading;
            for (int i = list.Count; i-- > 0;)
            {
                string name = list[i].packageIdPlayerFacingInt;
                if (name == "Haplo.Miscellaneous.Robots")
				{
					Print(name);
					return;
				}
				if (name == "RH2.Faction.VOID")
				{
					Print(name);
					return;
				}
			}
			safetyNeeded = false;

			static void Print(string mod)
			{
				Log.Warning($"[Clean Pathfinding] the mod {mod} is partially incompatible. The 'road attraction' calculations will be skipped.");
			}
		}
	}
    public class Mod_CleanPathfinding : Mod
	{
		public static Dictionary<string, bool> patchLedger = new Dictionary<string, bool>();

		public Mod_CleanPathfinding(ModContentPack content) : base(content)
		{
			base.GetSettings<ModSettings_CleanPathfinding>();
			new Harmony(this.Content.PackageIdPlayerFacing).PatchAll();
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			//========Setup tabs=========
			GUI.BeginGroup(inRect);
			var tabs = new List<TabRecord>();
			tabs.Add(new TabRecord("CleanPathfinding.Settings.Header.Tuning".Translate(), delegate { selectedTab = Tab.tuning; }, selectedTab == Tab.tuning));
			tabs.Add(new TabRecord("CleanPathfinding.Settings.Header.Doorpathing".Translate(), delegate { selectedTab = Tab.doorPathing; }, selectedTab == Tab.doorPathing));
			tabs.Add(new TabRecord("CleanPathfinding.Settings.Header.Rules".Translate(), delegate { selectedTab = Tab.rules; }, selectedTab == Tab.rules));
			tabs.Add(new TabRecord("CleanPathfinding.Settings.Header.Misc".Translate(), delegate { selectedTab = Tab.misc; }, selectedTab == Tab.misc));

			Rect rect = new Rect(0f, 32f, inRect.width, inRect.height - 32f);
			Widgets.DrawMenuSection(rect);
			TabDrawer.DrawTabs(new Rect(0f, 32f, inRect.width, Text.LineHeight), tabs);

			if (selectedTab == Tab.tuning) DrawTuning();
			else if (selectedTab == Tab.doorPathing) DrawDoorpathing();
			else if (selectedTab == Tab.rules) DrawRules();
			else DrawMisc();
			GUI.EndGroup();

			void DrawTuning()
			{
				Listing_Standard options = new Listing_Standard();
				options.Begin(inRect.ContractedBy(15f));
				var before = enableTuning;
				options.CheckboxLabeled("CleanPathfinding.Settings.EnableTuning".Translate(), ref enableTuning, "CleanPathfinding.Settings.EnableTuning.Desc".Translate());
				if (before != enableTuning)
				{
					//TODO: Need some sorta default resetter method
					bias = 5;
					naturalBias = 0;
					roadBias = 9;
					heuristicAdjuster = 90;
					regionPathing = true;
					regionModeThreshold = 1000;
				}
				options.GapLine();
				options.End();
				options.Begin(new Rect(inRect.x + 15, inRect.y + 55, inRect.width - 30, inRect.height - 30));

				if (enableTuning)
				{
					options.Label("CleanPathfinding.Settings.Bias".Translate("5", "0", "12") + bias, -1f, "CleanPathfinding.Settings.Bias.Desc".Translate());
					bias = (int)options.Slider((float)bias, 0f, 12f);

					options.Label("CleanPathfinding.Settings.NaturalBias".Translate("0", "0", "12") + naturalBias, -1f, "CleanPathfinding.Settings.NaturalBias.Desc".Translate());
					naturalBias = (int)options.Slider((float)naturalBias, 0f, 12f);

					options.Label("CleanPathfinding.Settings.RoadBias".Translate("9", "0", "12") + roadBias, -1f, "CleanPathfinding.Settings.RoadBias.Desc".Translate());
					roadBias = (int)options.Slider((float)roadBias, 0f, 12f);

					options.Label("CleanPathfinding.Settings.HeuristicAdjuster".Translate("90", "0", "200", heuristicAdjuster == 200 ? "Max".Translate() : heuristicAdjuster), -1f, "CleanPathfinding.Settings.HeuristicAdjuster.Desc".Translate());
					heuristicAdjuster = (int)options.Slider((float)heuristicAdjuster, 0f, 200f);
					
					options.CheckboxLabeled("CleanPathfinding.Settings.EnableRegionPathing".Translate(), ref regionPathing, "CleanPathfinding.Settings.EnableRegionPathing.Desc".Translate() + "CleanPathfinding.Settings.RegionModeThreshold.Desc".Translate());
					if (regionPathing)
					{
						if (regionModeThreshold == 100000) regionModeThreshold = 1000; //Reset to default if just enabled
						options.Label("CleanPathfinding.Settings.RegionModeThreshold".Translate("1000", "500", "2000") + regionModeThreshold, -1f, "CleanPathfinding.Settings.RegionModeThreshold.Desc".Translate().CapitalizeFirst());
						regionModeThreshold = (int)options.Slider((float)regionModeThreshold, 500f, 2000f);
					}
					else regionModeThreshold = 100000;
				}
				else
				{
					bias = naturalBias = roadBias = 0;
					regionModeThreshold = 100000;
					regionPathing = false;
				}
				options.End();
			}
			
			void DrawDoorpathing()
			{
				Listing_Standard options = new Listing_Standard();
				options.Begin(inRect.ContractedBy(15f));
				if (Current.ProgramState != ProgramState.Playing) 
				{
					options.CheckboxLabeled("CleanPathfinding.Settings.DoorPathing".Translate(), ref doorPathing, "CleanPathfinding.Settings.DoorPathing.Desc".Translate());
				}
				else
				{
					options.Label("CleanPathfinding.Settings.DoorPathing.Notice".Translate());
				}
				options.GapLine();
				options.End();
				options.Begin(new Rect(inRect.x + 15, inRect.y + 55, inRect.width - 30, inRect.height - 30));

				if (doorPathing)
				{
					options.Label("CleanPathfinding.Settings.DoorPathingSide".Translate("250", "50", "500") + doorPathingSide, -1f, "CleanPathfinding.Settings.DoorPathingSide.Desc".Translate());
					doorPathingSide = (int)options.Slider((float)doorPathingSide, 50f, 500f);
					options.Label("CleanPathfinding.Settings.DoorPathingEmergency".Translate("500", "500", "1000") + doorPathingEmergency, -1f, "CleanPathfinding.Settings.DoorPathingEmergency.Desc".Translate());
					doorPathingEmergency = (int)options.Slider((float)doorPathingEmergency, 500f, 1000f);
				}

				options.End();
			}

			void DrawRules()
			{
				Listing_Standard options = new Listing_Standard();
				options.Begin(new Rect(inRect.x + 15, inRect.y + 55, inRect.width - 30, inRect.height - 30));

				options.CheckboxLabeled("CleanPathfinding.Settings.FactorCarryingPawn".Translate(), ref factorCarryingPawn, "CleanPathfinding.Settings.FactorCarryingPawn.Desc".Translate());
				options.CheckboxLabeled("CleanPathfinding.Settings.FactorBleeding".Translate(), ref factorBleeding, "CleanPathfinding.Settings.FactorBleeding.Desc".Translate());
				
				options.Label("CleanPathfinding.Settings.DarknessPenalty".Translate("2", "0", "6") + (darknessPenalty == 0f ? "Off".Translate() : darknessPenalty), -1f, "CleanPathfinding.Settings.DarknessPenalty.Desc".Translate());
				darknessPenalty = (int)options.Slider((float)darknessPenalty, 0, 6);
				factorLight = darknessPenalty != 0f;

				options.End();
			}

			void DrawMisc()
			{
				Listing_Standard options = new Listing_Standard();
				options.Begin(new Rect(inRect.x + 15, inRect.y + 55, inRect.width - 30, inRect.height - 30));

				
				options.Label("CleanPathfinding.Settings.ExitRange".Translate("0", "0", "200") + (exitRange == 0f ? "Off".Translate() : exitRange), -1f, "CleanPathfinding.Settings.ExitRange.Desc".Translate());
				exitRange = (int)options.Slider((float)exitRange, 0f, 200f);
				exitTuning = exitRange > 0f;
				
				float wanderDelayRounded = (float)System.Math.Round((wanderDelay / 60f), 1);
				options.Label("CleanPathfinding.Settings.WanderDelay".Translate("0", "-2", "10", 
					wanderTuning ? (wanderDelayRounded.ToString() + "Seconds".Translate()) : "Off".Translate() ), -1f, "CleanPathfinding.Settings.WanderDelay.Desc".Translate());
				wanderDelay = (int)(options.Slider((float)wanderDelay, -118f, 600f));
				wanderTuning = wanderDelay < -20f || wanderDelay > 20f;
				
				options.CheckboxLabeled("CleanPathfinding.Settings.OptimizeCollider".Translate(), ref optimizeCollider, "CleanPathfinding.Settings.OptimizeCollider.Desc".Translate());
				if (Prefs.DevMode) options.CheckboxLabeled("DevMode: Enable logging", ref logging, null);

				options.End();
			}
		}

		public override string SettingsCategory()
		{
			return "Clean Pathfinding";
		}

		public override void WriteSettings()
		{
			base.WriteSettings();
			var harmony = new Harmony(this.Content.PackageIdPlayerFacing);
			
			try
			{
				CleanPathfindingUtility.UpdatePathCosts();
				DoorPathingUtility.UpdateAllDoorsOnAllMaps();
			}
			catch (System.Exception ex)
			{
				Log.Message("[Clean Pathfinding] Error processing settings: " + ex);
			}
			

			//Wander tuning patcher/unpatcher
			//To-do: wrap all this up into a nice clean universal method
			try
			{
				//Wander tuning
				if (!wanderTuning && patchLedger[nameof(Patch_JobGiver_Wander)])
				{
					patchLedger[nameof(Patch_JobGiver_Wander)] = false;
					harmony.Unpatch(AccessTools.Method(typeof(JobGiver_Wander), nameof(JobGiver_Wander.TryGiveJob) ), HarmonyPatchType.Postfix, this.Content.PackageIdPlayerFacing);
				}
				else if (wanderTuning && !patchLedger[nameof(Patch_JobGiver_Wander)])
				{
					patchLedger[nameof(Patch_JobGiver_Wander)] = true;
					harmony.Patch(AccessTools.Method(typeof(JobGiver_Wander), nameof(JobGiver_Wander.TryGiveJob) ), 
						postfix: new HarmonyMethod(typeof(Patch_JobGiver_Wander), nameof(Patch_JobGiver_Wander.Postfix)));
				}

				//Doorpathing
				if (!doorPathing && patchLedger[nameof(Patch_Building_Door_GetGizmos)])
				{
					patchLedger[nameof(Patch_Building_Door_GetGizmos)] = false;
					harmony.Unpatch(AccessTools.Method(typeof(Building_Door), nameof(Building_Door.GetGizmos) ), HarmonyPatchType.Postfix, this.Content.PackageIdPlayerFacing);
					harmony.Unpatch(AccessTools.Method(typeof(Building_Door), nameof(Building_Door.DeSpawn) ), HarmonyPatchType.Prefix, this.Content.PackageIdPlayerFacing);
					harmony.Unpatch(AccessTools.Method(typeof(Room), nameof(Room.Notify_RoomShapeChanged) ), HarmonyPatchType.Postfix, this.Content.PackageIdPlayerFacing);
				}
				else if (doorPathing && !patchLedger[nameof(Patch_Building_Door_GetGizmos)])
				{
					patchLedger[nameof(Patch_Building_Door_GetGizmos)] = true;
					harmony.Patch(AccessTools.Method(typeof(Building_Door), nameof(Building_Door.GetGizmos) ), 
						postfix: new HarmonyMethod(typeof(Patch_Building_Door_GetGizmos), nameof(Patch_Building_Door_GetGizmos.Postfix)));
					harmony.Patch(AccessTools.Method(typeof(Building_Door), nameof(Building_Door.DeSpawn) ), 
						postfix: new HarmonyMethod(typeof(Patch_Building_DoorDeSpawn), nameof(Patch_Building_DoorDeSpawn.Prefix)));
					harmony.Patch(AccessTools.Method(typeof(Room), nameof(Room.Notify_RoomShapeChanged) ), 
						postfix: new HarmonyMethod(typeof(Patch_Notify_RoomShapeChanged), nameof(Patch_Notify_RoomShapeChanged.Postfix)));
				}
			}
			catch (System.Exception ex)
			{                
				Log.Error("[Clean Pathfinding] Error processing patching or unpatching, skipping...\n" + ex);
			}
		}
	}

	public class ModSettings_CleanPathfinding : ModSettings
	{
		public override void ExposeData()
		{
			Scribe_Values.Look(ref bias, "bias", 5);
			Scribe_Values.Look(ref naturalBias, "naturalBias", 0);
			Scribe_Values.Look(ref roadBias, "roadBias", 9);
			Scribe_Values.Look(ref regionModeThreshold, "regionModeThreshold", 100000);
			Scribe_Values.Look(ref heuristicAdjuster, "heuristicAdjuster", 90);
			Scribe_Values.Look(ref darknessPenalty, "darknessPenalty", 2);
			Scribe_Values.Look(ref factorLight, "factorLight", true);
			Scribe_Values.Look(ref factorCarryingPawn, "factorCarryingPawn", true);
			Scribe_Values.Look(ref factorBleeding, "factorBleeding", true);
			Scribe_Values.Look(ref exitRange, "exitRange");
			Scribe_Values.Look(ref doorPathing, "doorPathing", true);
			Scribe_Values.Look(ref doorPathingSide, "doorPathingSide", 250);
			Scribe_Values.Look(ref doorPathingEmergency, "doorPathingEmergency", 500);
			Scribe_Values.Look(ref wanderDelay, "wanderDelay");
			Scribe_Values.Look(ref optimizeCollider, "optimizeCollider", true);
			Scribe_Values.Look(ref exitTuning, "exitTuning");
			Scribe_Values.Look(ref wanderTuning, "wanderTuning");
			Scribe_Values.Look(ref regionPathing, "regionPathing", true);
			Scribe_Values.Look(ref enableTuning, "enableTuning", true);
			base.ExposeData();
		}

		static public int bias = 8,
			naturalBias,
			roadBias = 9,
			exitRange,
			doorPathingSide = 250,
			doorPathingEmergency = 500,
			wanderDelay = 0,
			regionModeThreshold = 1000,
			heuristicAdjuster = 90,
			darknessPenalty = 2;
		static public bool factorLight = true,
			factorCarryingPawn = true,
			factorBleeding = true,
			logging,
			doorPathing = true,
			optimizeCollider = true,
			exitTuning,
			wanderTuning,
			regionPathing = true,
			enableTuning = true;
		public static Vector2 scrollPos = Vector2.zero;

		public static Tab selectedTab = Tab.tuning;
		public enum Tab { tuning, doorPathing, rules, misc };
	}
}
