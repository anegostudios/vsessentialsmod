using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

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


        public List<PathNode> FindPath(BlockPos start, BlockPos end, int maxFallHeight, float stepHeight, Cuboidf entityCollBox)
        {
            return astar.FindPath(start, end, maxFallHeight, stepHeight, entityCollBox, 9999, 1);
        }

        public List<Vec3d> FindPathAsWaypoints(BlockPos start, BlockPos end, int maxFallHeight, float stepHeight, Cuboidf entityCollBox, int searchDepth = 9999, int mhdistanceTolerance = 0)
        {
            return astar.FindPathAsWaypoints(start, end, maxFallHeight, stepHeight, entityCollBox, searchDepth, mhdistanceTolerance);
        }

        public List<Vec3d> FindPathAsWaypoints(PathfinderTask task)
        {
            return astar.FindPathAsWaypoints(task.startBlockPos, task.targetBlockPos, task.maxFallHeight, task.stepHeight, task.collisionBox, task.searchDepth, task.mhdistanceTolerance);
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
