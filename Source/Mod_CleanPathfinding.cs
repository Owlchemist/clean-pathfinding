
using Verse;
using HarmonyLib;
using UnityEngine;
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
			Listing_Standard options = new Listing_Standard();
			options.Begin(inRect);
			options.Gap();
			options.Label("CleanPathfinding.Settings.Header.Tuning".Translate());
			options.GapLine(); //======================================

			options.Label("CleanPathfinding.Settings.Bias".Translate("8", "0", "30") + bias, -1f, "CleanPathfinding.Settings.Bias.Desc".Translate());
			bias = (int)options.Slider((float)bias, 0f, 30f);

			options.Label("CleanPathfinding.Settings.NaturalBias".Translate("4", "0", "30") + naturalBias, -1f, "CleanPathfinding.Settings.NaturalBias.Desc".Translate());
			naturalBias = (int)options.Slider((float)naturalBias, 0f, 30f);

			options.Label("CleanPathfinding.Settings.RoadBias".Translate("4", "0", "4") + roadBias, -1f, "CleanPathfinding.Settings.RoadBias.Desc".Translate());
			roadBias = (int)options.Slider((float)roadBias, 0f, 4f);

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
			options.Label("CleanPathfinding.Settings.ExitRange.Warning".Translate());
			options.Label("CleanPathfinding.Settings.ExitRange".Translate("0", "0", "200") + exitRange, -1f, "CleanPathfinding.Settings.ExitRange.Desc".Translate());
			exitRange = (int)options.Slider((float)exitRange, 0f, 200f);
			if (Prefs.DevMode) options.CheckboxLabeled("DevMode: Enable logging", ref logging, null);
			options.End();
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
			base.ExposeData();
		}

		static public int bias = 8, naturalBias = 4, roadBias = 4, extraRange, exitRange;
		static public bool factorLight = true, factorCarryingPawn = true, factorBleeding = true, logging;
	}
}
