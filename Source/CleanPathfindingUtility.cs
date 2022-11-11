
using Verse;
using System.Linq;
using System.Collections.Generic;
using static CleanPathfinding.ModSettings_CleanPathfinding;
 
namespace CleanPathfinding
{
    public static class CleanPathfindingUtility
	{	
		public static Dictionary<ushort, int[]> terrainCache = new Dictionary<ushort, int[]>();
		public static SimpleCurve Custom_DistanceCurve;
		static List<string> report = new List<string>();
		public static void Setup()
		{
			foreach (var terrainDef in DefDatabase<TerrainDef>.AllDefsListForReading)
			{
				if
				(
					terrainDef.generatedFilth != null || //Generates filth?
					(terrainDef.tags?.Contains("Road") ?? false) || //Is a road?
					(terrainDef.generatedFilth == null && (terrainDef.defName.Contains("_Rough") || terrainDef.defName.Contains("_RoughHewn"))) //Is clean but avoided regardless?
				) terrainCache.Add(terrainDef.shortHash, new int[] { terrainDef.extraNonDraftedPerceivedPathCost, 0 } );
			}
            UpdatePathCosts();
		}

		public static void UpdatePathCosts()
		{
			try
			{
				//Reset the cache
				report?.Clear();
				terrainCache?.ToList().ForEach(x => x.Value[1] = 0);

				foreach (var terrainDef in DefDatabase<TerrainDef>.AllDefsListForReading)
				{
					ushort hash = terrainDef.shortHash;
					//Reset to original value
					if (terrainCache.ContainsKey(hash)) terrainDef.extraNonDraftedPerceivedPathCost = terrainCache[hash][0];
					
					//Avoid filth
					if (bias > 0 && terrainDef.generatedFilth != null)
					{
						terrainDef.extraNonDraftedPerceivedPathCost += bias; 
						terrainCache[hash][1] = bias;
					}

					//Clean but natural terrain bias
					if (naturalBias > 0 && terrainDef.generatedFilth == null && (terrainDef.defName.Contains("_Rough") || terrainDef.defName.Contains("_RoughHewn")))
					{
						terrainDef.extraNonDraftedPerceivedPathCost += naturalBias; 
						terrainCache[hash][1] += naturalBias;
					}

					//Attraction to roads
					if (roadBias > 0 && (terrainDef.tags?.Contains("Road") ?? false))
					{
						terrainDef.extraNonDraftedPerceivedPathCost -= roadBias;
						terrainCache[hash][1] -= roadBias;
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
	}
}