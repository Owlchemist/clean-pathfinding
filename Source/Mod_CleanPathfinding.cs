
using Verse;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using static CleanPathfinding.ModSettings_CleanPathfinding;
using static CleanPathfinding.CleanPathfindingUtility;
 
namespace CleanPathfinding
{
    public class Mod_CleanPathfinding : Mod
	{

		public Mod_CleanPathfinding(ModContentPack content) : base(content)
		{
			new Harmony(this.Content.PackageIdPlayerFacing).PatchAll();
			base.GetSettings<ModSettings_CleanPathfinding>();
			LongEventHandler.QueueLongEvent(() => Setup(), null, false, null);
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

			options.Label("CleanPathfinding.Settings.RoadBias".Translate("8", "0", "8") + roadBias, -1f, "CleanPathfinding.Settings.RoadBias.Desc".Translate());
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
			options.CheckboxLabeled("CleanPathfinding.Settings.DoorPathing".Translate(), ref doorPathing, "CleanPathfinding.Settings.DoorPathing.Desc".Translate());
			if (doorPathing)
			{
				options.Label("CleanPathfinding.Settings.DoorPathingSide".Translate("400", "100", "1000") + doorPathingSide, -1f, "CleanPathfinding.Settings.DoorPathingSide.Desc".Translate());
				doorPathingSide = (int)options.Slider((float)doorPathingSide, 100f, 1000f);
				options.Label("CleanPathfinding.Settings.DoorPathingEmergency".Translate("1500", "1000", "3000") + doorPathingEmergency, -1f, "CleanPathfinding.Settings.DoorPathingEmergency.Desc".Translate());
				doorPathingEmergency = (int)options.Slider((float)doorPathingEmergency, 100f, 3000f);
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
				wanderDelay = (int)(options.Slider((float)wanderDelay, -120f, 600f));
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
			UpdatePathCosts();
			DoorPathingUtility.RecalculateAllDoors();
		}
	}

	public class ModSettings_CleanPathfinding : ModSettings
	{
		public override void ExposeData()
		{
			Scribe_Values.Look<int>(ref bias, "bias", 8, false);
			Scribe_Values.Look<int>(ref naturalBias, "naturalBias", 4, false);
			Scribe_Values.Look<int>(ref roadBias, "roadBias", 4, false);
			Scribe_Values.Look<int>(ref extraRange, "extraRange", 0, false);
			Scribe_Values.Look<bool>(ref factorLight, "factorLight", true, false);
			Scribe_Values.Look<bool>(ref factorCarryingPawn, "factorCarryingPawn", true, false);
			Scribe_Values.Look<bool>(ref factorBleeding, "factorBleeding", true, false);
			Scribe_Values.Look<int>(ref exitRange, "exitRange", 0, false);
			Scribe_Values.Look<bool>(ref doorPathing, "doorPathing", true, false);
			Scribe_Values.Look<int>(ref doorPathingSide, "doorPathingSide", 400, false);
			Scribe_Values.Look<int>(ref doorPathingEmergency, "doorPathingEmergency", 1500, false);
			Scribe_Values.Look<int>(ref wanderDelay, "wanderDelay", 0, false);
			Scribe_Values.Look<bool>(ref optimizeCollider, "optimizeCollider", true, false);
			Scribe_Values.Look<bool>(ref exitTuning, "exitTuning", false, false);
			Scribe_Values.Look<bool>(ref wanderTuning, "wanderTuning", false, false);
			base.ExposeData();
		}

		static public int bias = 8, naturalBias = 4, roadBias = 4, extraRange, exitRange, doorPathingSide = 400, doorPathingEmergency = 1500, wanderDelay = 0;
		static public bool factorLight = true, factorCarryingPawn = true, factorBleeding = true, logging, doorPathing = true, optimizeCollider = true, exitTuning = false, wanderTuning = false;
		public static Vector2 scrollPos = Vector2.zero;
	}
}
