using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

#nullable disable

namespace Vintagestory.Essentials
{
    public class PathfindSystem : ModSystem
    {
        public AStar astar;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            astar = new AStar(api);
        }


        /// <summary>
        /// Finds an escape path away from fleeFromPos
        /// </summary>
        /// <param name="start"></param>
        /// <param name="fleeFromPos"></param>
        /// <param name="modeMinimumFleeDistance">Must be >0</param>
        /// <param name="maxFallHeight"></param>
        /// <param name="stepHeight"></param>
        /// <param name="entityCollBox"></param>
        /// <param name="searchDepth"></param>
        /// <param name="creatureType"></param>
        /// <returns></returns>
        public virtual List<PathNode> FindEscapePath(BlockPos start, BlockPos fleeFromPos, float modeMinimumFleeDistance, int maxFallHeight, float stepHeight, Cuboidf entityCollBox, int searchDepth = 9999, EnumAICreatureType creatureType = EnumAICreatureType.Default)
        {
            return astar.FindPathOrEscapePath(start, fleeFromPos, modeMinimumFleeDistance, maxFallHeight, stepHeight, entityCollBox, 9999, 0, creatureType);
        }

        public List<PathNode> FindPath(BlockPos start, BlockPos end, int maxFallHeight, float stepHeight, Cuboidf entityCollBox, EnumAICreatureType creatureType = EnumAICreatureType.Default)
        {
            return astar.FindPath(start, end, maxFallHeight, stepHeight, entityCollBox, 9999, 1, creatureType);
        }

        /// <summary>
        /// Finds a path to "end" or away from "end" if modeMinimumFleeDistance>0
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="modeMinimumFleeDistance"></param>
        /// <param name="maxFallHeight"></param>
        /// <param name="stepHeight"></param>
        /// <param name="entityCollBox"></param>
        /// <param name="searchDepth"></param>
        /// <param name="mhdistanceTolerance"></param>
        /// <param name="creatureType"></param>
        /// <returns></returns>
        public List<Vec3d> FindPathAsWaypoints(BlockPos start, BlockPos end, float modeMinimumFleeDistance, int maxFallHeight, float stepHeight, Cuboidf entityCollBox, int searchDepth = 9999, int mhdistanceTolerance = 0, EnumAICreatureType creatureType = EnumAICreatureType.Default)
        {
            return astar.FindPathAsWaypoints(start, end, modeMinimumFleeDistance, maxFallHeight, stepHeight, entityCollBox, searchDepth, mhdistanceTolerance, creatureType);
        }

        public List<Vec3d> FindPathAsWaypoints(PathfinderTask task)
        {
            return astar.FindPathAsWaypoints(task.startBlockPos, task.targetBlockPos, task.modeMinFleeDistance, task.maxFallHeight, task.stepHeight, task.collisionBox, task.searchDepth, task.mhdistanceTolerance);
        }

        public List<Vec3d> ToWaypoints(List<PathNode> nodes)
        {
            return astar.ToWaypoints(nodes);
        }

        public override void Dispose()
        {
            astar?.Dispose();   // astar will be null clientside, as this ModSystem is only started on servers
            astar = null;
        }
    }
}
