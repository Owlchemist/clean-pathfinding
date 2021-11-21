
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
			DefDatabase<TerrainDef>.AllDefsListForReading.ForEach
			(x => 
	            {
					if
					(
						x.generatedFilth != null || //Generates filth?
						(x.tags?.Contains("Road") ?? false) || //Is a road?
						(x.generatedFilth == null && (x.defName.Contains("_Rough") || x.defName.Contains("_RoughHewn"))) //Is clean but avoided regardless?
					)
					{
						terrainCache.Add(x.shortHash, new int[] { x.extraNonDraftedPerceivedPathCost, 0 } );
						//Log.Message(x.defName + " is " + x.shortHash.ToString());
					}
            	}
			);
            UpdatePathCosts();
		}

		public static void UpdatePathCosts()
		{
			//Reset the cache
			report?.Clear();
			terrainCache?.ToList().ForEach(x => x.Value[1] = 0);

			DefDatabase<TerrainDef>.AllDefsListForReading.ForEach
			(x => 
				{
					var hash = x.shortHash;
					//Reset to original value
					if (terrainCache.ContainsKey(hash)) x.extraNonDraftedPerceivedPathCost = terrainCache[hash][0];
					
					//Avoid filth
					if (bias > 0 && x.generatedFilth != null)
					{
						x.extraNonDraftedPerceivedPathCost += bias; 
						terrainCache[hash][1] = bias;
					}

					//Clean but natural terrain bias
					if (naturalBias > 0 && x.generatedFilth == null && (x.defName.Contains("_Rough") || x.defName.Contains("_RoughHewn")))
					{
						x.extraNonDraftedPerceivedPathCost += naturalBias; 
						terrainCache[hash][1] += naturalBias;
					}

					//Attraction to roads
					if (roadBias > 0 && (x.tags?.Contains("Road") ?? false))
					{
						x.extraNonDraftedPerceivedPathCost -= roadBias;
						terrainCache[hash][1] -= roadBias;
					}

					//Debug
					if (logging && Prefs.DevMode)
					{
						report.Add(x.defName + ": " + x.extraNonDraftedPerceivedPathCost);
					}
				}
			);

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
	}
}