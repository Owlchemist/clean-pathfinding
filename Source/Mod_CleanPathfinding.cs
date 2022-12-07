
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
        static Setup()
        {
            foreach (var terrainDef in DefDatabase<TerrainDef>.AllDefsListForReading)
			{
				if
				(
					terrainDef.generatedFilth != null || //Generates filth?
					(terrainDef.tags?.Contains("CleanPath") ?? false) || //Is a road?
					(terrainDef.generatedFilth == null && (terrainDef.defName.Contains("_Rough"))) //Is clean but avoided regardless?
				) CleanPathfindingUtility.terrainCache.Add(terrainDef.index, new int[] { terrainDef.extraNonDraftedPerceivedPathCost, 0 } );
			}
            CleanPathfindingUtility.UpdatePathCosts();
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
			inRect.yMin += 20f;
			inRect.yMax -= 20f;
			Listing_Standard options = new Listing_Standard();
			Rect outRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
			Rect rect = new Rect(0f, 0f, inRect.width - 30f, inRect.height * 1.5f);
			Widgets.BeginScrollView(outRect, ref scrollPos, rect, true);

			options.Begin(rect);
			options.Gap();
			options.Label("CleanPathfinding.Settings.Header.Tuning".Translate());
			options.GapLine(); //======================================
			options.Label("CleanPathfinding.Settings.Bias".Translate("8", "0", "12") + bias, -1f, "CleanPathfinding.Settings.Bias.Desc".Translate());
			bias = (int)options.Slider((float)bias, 0f, 12f);

			options.Label("CleanPathfinding.Settings.NaturalBias".Translate("4", "0", "12") + naturalBias, -1f, "CleanPathfinding.Settings.NaturalBias.Desc".Translate());
			naturalBias = (int)options.Slider((float)naturalBias, 0f, 12f);

			options.Label("CleanPathfinding.Settings.RoadBias".Translate("6", "0", "8") + roadBias, -1f, "CleanPathfinding.Settings.RoadBias.Desc".Translate());
			roadBias = (int)options.Slider((float)roadBias, 0f, 8f);

			options.Label("CleanPathfinding.Settings.ExtraRange".Translate("0", "0", "230") + extraRange, -1f, "CleanPathfinding.Settings.ExtraRange.Desc".Translate());
			extraRange = (int)options.Slider((float)extraRange, 0f, 230f);

			options.Gap();
			options.Label("CleanPathfinding.Settings.Header.Rules".Translate());
			options.GapLine(); //======================================
			options.CheckboxLabeled("CleanPathfinding.Settings.FactorLight".Translate(), ref factorLight, "CleanPathfinding.Settings.FactorLight.Desc".Translate());
			options.CheckboxLabeled("CleanPathfinding.Settings.FactorCarryingPawn".Translate(), ref factorCarryingPawn, "CleanPathfinding.Settings.FactorCarryingPawn.Desc".Translate());
			options.CheckboxLabeled("CleanPathfinding.Settings.FactorBleeding".Translate(), ref factorBleeding, "CleanPathfinding.Settings.FactorBleeding.Desc".Translate());

			options.Gap();
			options.Label("CleanPathfinding.Settings.Header.Misc".Translate());
			options.GapLine(); //======================================
			options.CheckboxLabeled("CleanPathfinding.Settings.OptimizeCollider".Translate(), ref optimizeCollider, "CleanPathfinding.Settings.OptimizeCollider.Desc".Translate());
			if (Current.ProgramState != ProgramState.Playing) options.CheckboxLabeled("CleanPathfinding.Settings.DoorPathing".Translate(), ref doorPathing, "CleanPathfinding.Settings.DoorPathing.Desc".Translate());
			if (doorPathing)
			{
				options.Label("CleanPathfinding.Settings.DoorPathingSide".Translate("250", "50", "500") + doorPathingSide, -1f, "CleanPathfinding.Settings.DoorPathingSide.Desc".Translate());
				doorPathingSide = (int)options.Slider((float)doorPathingSide, 50f, 500f);
				options.Label("CleanPathfinding.Settings.DoorPathingEmergency".Translate("500", "500", "1000") + doorPathingEmergency, -1f, "CleanPathfinding.Settings.DoorPathingEmergency.Desc".Translate());
				doorPathingEmergency = (int)options.Slider((float)doorPathingEmergency, 500f, 1000f);
			}
			options.CheckboxLabeled("CleanPathfinding.Settings.EnableExitTuning".Translate(), ref exitTuning, "CleanPathfinding.Settings.ExitRange.Warning".Translate());
			if (exitTuning)
			{
				options.Label("CleanPathfinding.Settings.ExitRange".Translate("0", "0", "200") + exitRange, -1f, "CleanPathfinding.Settings.ExitRange.Desc".Translate());
				exitRange = (int)options.Slider((float)exitRange, 0f, 200f);
			}
			options.CheckboxLabeled("CleanPathfinding.Settings.EnableWanderTuning".Translate(), ref wanderTuning, "CleanPathfinding.Settings.EnableWanderTuning.Desc".Translate());
			if (wanderTuning)
			{
				options.Label("CleanPathfinding.Settings.WanderDelay".Translate("0", "-2", "10", (int)(wanderDelay / 60f)), -1f, "CleanPathfinding.Settings.WanderDelay.Desc".Translate());
				wanderDelay = (int)(options.Slider((float)wanderDelay, -118f, 600f));
			}
			if (Prefs.DevMode) options.CheckboxLabeled("DevMode: Enable logging", ref logging, null);

			options.End();
			Widgets.EndScrollView();
			base.DoSettingsWindowContents(inRect);
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
				if (!doorPathing && patchLedger[nameof(Patch_Building_Door)])
				{
					patchLedger[nameof(Patch_Building_Door)] = false;
					harmony.Unpatch(AccessTools.Method(typeof(Building_Door), nameof(Building_Door.GetGizmos) ), HarmonyPatchType.Postfix, this.Content.PackageIdPlayerFacing);
					harmony.Unpatch(AccessTools.Method(typeof(Building_Door), nameof(Building_Door.DeSpawn) ), HarmonyPatchType.Prefix, this.Content.PackageIdPlayerFacing);
					harmony.Unpatch(AccessTools.Method(typeof(Room), nameof(Room.Notify_RoomShapeChanged) ), HarmonyPatchType.Postfix, this.Content.PackageIdPlayerFacing);
				}
				else if (doorPathing && !patchLedger[nameof(Patch_Building_Door)])
				{
					patchLedger[nameof(Patch_Building_Door)] = true;
					harmony.Patch(AccessTools.Method(typeof(Building_Door), nameof(Building_Door.GetGizmos) ), 
						postfix: new HarmonyMethod(typeof(Patch_Building_Door), nameof(Patch_Building_Door.Postfix)));
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
			Scribe_Values.Look<int>(ref bias, "bias", 8);
			Scribe_Values.Look<int>(ref naturalBias, "naturalBias", 4);
			Scribe_Values.Look<int>(ref roadBias, "roadBias", 4);
			Scribe_Values.Look<int>(ref extraRange, "extraRange");
			Scribe_Values.Look<bool>(ref factorLight, "factorLight", true);
			Scribe_Values.Look<bool>(ref factorCarryingPawn, "factorCarryingPawn", true);
			Scribe_Values.Look<bool>(ref factorBleeding, "factorBleeding", true);
			Scribe_Values.Look<int>(ref exitRange, "exitRange");
			Scribe_Values.Look<bool>(ref doorPathing, "doorPathing", true);
			Scribe_Values.Look<int>(ref doorPathingSide, "doorPathingSide", 400);
			Scribe_Values.Look<int>(ref doorPathingEmergency, "doorPathingEmergency", 1500);
			Scribe_Values.Look<int>(ref wanderDelay, "wanderDelay");
			Scribe_Values.Look<bool>(ref optimizeCollider, "optimizeCollider", true);
			Scribe_Values.Look<bool>(ref exitTuning, "exitTuning");
			Scribe_Values.Look<bool>(ref wanderTuning, "wanderTuning");
			base.ExposeData();
		}

		static public int bias = 8, naturalBias = 4, roadBias = 4, extraRange, exitRange, doorPathingSide = 400, doorPathingEmergency = 1500, wanderDelay = 0;
		static public bool factorLight = true, factorCarryingPawn = true, factorBleeding = true, logging, doorPathing = true, optimizeCollider = true, exitTuning = false, wanderTuning = false;
		public static Vector2 scrollPos = Vector2.zero;
	}
}
