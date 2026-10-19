using RoR2;
using RoR2.Navigation;
using UnityEngine;

namespace Ror2Common
{
    // The lowest ground a player can stand on in the stage: the lowest active ground node of its
    // navigation graph. A body is only "off the map" below that, wherever it last stood, so a long
    // drop onto lower ground, or a stage's out-of-bounds zone met above its floor, is not a fall out of it.
    // Computed once per stage; a stage with no ground graph has no floor (null), and callers then
    // go by their own rules.
    public static class StageFloor
    {
        /// How far below the lowest node a body must be, in metres, to count as below the floor.
        public const float Margin = 2f;

        static NodeGraph cachedGraph;
        static float cachedLowest;
        static bool cachedFound;

        /// The lowest standing height of this stage, if it has a ground graph with nodes.
        public static bool TryLowest(out float y)
        {
            y = 0f;
            var graph = SceneInfo.instance ? SceneInfo.instance.groundNodes : null;
            if (!graph) return false;
            if (graph != cachedGraph)
            {
                cachedGraph = graph;
                cachedFound = false;
                float lowest = float.PositiveInfinity;
                var nodes = graph.GetActiveNodesForHullMask(HullMask.Human);
                for (int i = 0; i < nodes.Count; i++)
                    if (graph.GetNodePosition(nodes[i], out var at) && at.y < lowest) lowest = at.y;
                if (!float.IsInfinity(lowest)) { cachedLowest = lowest; cachedFound = true; }
            }
            y = cachedLowest;
            return cachedFound;
        }

        /// True when `feet` is below the stage's lowest ground (or the stage has no ground graph to say).
        public static bool Below(Vector3 feet) => !TryLowest(out float y) || feet.y < y - Margin;
    }
}
