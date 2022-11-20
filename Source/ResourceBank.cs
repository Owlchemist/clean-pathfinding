using UnityEngine;
using Verse;
 
namespace CleanPathfinding
{
	[StaticConstructorOnStartup]
	internal static class ResourceBank
	{
		public static readonly Texture2D iconPriority = ContentFinder<Texture2D>.Get("UI/Owl_Priority", true);
	}
}