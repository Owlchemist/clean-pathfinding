
using Verse;
using HarmonyLib;
using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using static CleanPathfinding.ModSettings_CleanPathfinding;
 
namespace CleanPathfinding
{
    public static class CleanPathfindingUtility
	{	
		public static Dictionary<int, int[]> terrainCache = new Dictionary<int, int[]>();
		public static SimpleCurve Custom_DistanceCurve;
		public static void Setup()
		{
			DefDatabase<TerrainDef>.AllDefs.Where(x => 
			x.generatedFilth != null
			|| (x.tags != null && x.tags.Contains("Road"))
			|| (x.generatedFilth == null && (x.defName.Contains("_Rough") || x.defName.Contains("_RoughHewn")))).ToList().ForEach(y => 
            {
                terrainCache.Add(y.GetHashCode(),new int[2]{ y.extraNonDraftedPerceivedPathCost, 0 });
                //Log.Message(y.defName + " is " + y.GetHashCode().ToString());
            });
            UpdatePathCosts();
		}

		public static void UpdatePathCosts()
		{
			//Reset the cache
			terrainCache?.ToList().ForEach(x => x.Value[1] = 0);
			DefDatabase<TerrainDef>.AllDefs.Where(x => terrainCache.ContainsKey(x.GetHashCode()))?.ToList().ForEach(y => 
				y.extraNonDraftedPerceivedPathCost = terrainCache[y.GetHashCode()][0]);

			//Avoid filth
			if (bias > 0)
			{
				DefDatabase<TerrainDef>.AllDefs.Where(x => x.generatedFilth != null).ToList().ForEach(y => 
				{
					y.extraNonDraftedPerceivedPathCost += bias; 
					terrainCache[y.GetHashCode()][1] = bias;
				});
			}
			if (naturalBias > 0)
			{
				//Targetting the renderPrecedence was the only way I could think to universally targe the procedurally generated terraindefs
				DefDatabase<TerrainDef>.AllDefs.Where(x => 
				x.generatedFilth == null && (x.defName.Contains("_Rough") || x.defName.Contains("_RoughHewn"))).ToList().ForEach(y => 
				{
					y.extraNonDraftedPerceivedPathCost += naturalBias; 
					terrainCache[y.GetHashCode()][1] += naturalBias;
				});
			}
			//Attraction to roads
			if (roadBias > 0)
			{
				DefDatabase<TerrainDef>.AllDefs.Where(x => x.tags != null && x.tags.Contains("Road")).ToList().ForEach(y => 
				{
					y.extraNonDraftedPerceivedPathCost -= roadBias;
					terrainCache[y.GetHashCode()][1] -= roadBias;
				});
			}

			List<string> report = new List<string>();
			//Debug
			if (logging && Prefs.DevMode)
			{
					DefDatabase<TerrainDef>.AllDefs.ToList().ForEach(x => report.Add(x.defName + ": " + x.extraNonDraftedPerceivedPathCost));
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
			if (Find.World != null) Find.Maps.ForEach(x => x.pathing.RecalculateAllPerceivedPathCosts());
		}
	}
}
