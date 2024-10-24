using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.Essentials
{
    public class AStar
    {
        protected ICoreServerAPI api;
        protected ICachingBlockAccessor blockAccess;

        public int NodesChecked;

        public double centerOffsetX = 0.5;
        public double centerOffsetZ = 0.5;
        public EnumAICreatureType creatureType;

        protected CollisionTester collTester;

        public AStar(ICoreServerAPI api)
        {
            this.api = api;
            collTester = new CollisionTester();
            blockAccess = api.World.GetCachingBlockAccessor(true, true);
        }

        public PathNodeSet openSet = new PathNodeSet();
        public HashSet<PathNode> closedSet = new HashSet<PathNode>();

        public virtual List<Vec3d> FindPathAsWaypoints(BlockPos start, BlockPos end, int maxFallHeight, float stepHeight, Cuboidf entityCollBox, int searchDepth = 9999, int mhdistanceTolerance = 0, EnumAICreatureType creatureType = EnumAICreatureType.Default)
        {
            List<PathNode> nodes = FindPath(start, end, maxFallHeight, stepHeight, entityCollBox, searchDepth, mhdistanceTolerance, creatureType);
            return nodes == null ? null : ToWaypoints(nodes);
        }

        public virtual List<PathNode> FindPath(BlockPos start, BlockPos end, int maxFallHeight, float stepHeight, Cuboidf entityCollBox, int searchDepth = 9999, int mhdistanceTolerance = 0, EnumAICreatureType creatureType = EnumAICreatureType.Default)
        {
            if (entityCollBox.XSize > 100 || entityCollBox.YSize > 100 || entityCollBox.ZSize > 100)
            {
                api.Logger.Warning("AStar:FindPath() was called with a entity box larger than 100 ({0}). Algo not designed for such sizes, likely coding error. Will ignore.", entityCollBox);
                return null;
            }

            this.creatureType = creatureType;
            blockAccess.Begin();

            centerOffsetX = 0.3 + api.World.Rand.NextDouble() * 0.4;
            centerOffsetZ = 0.3 + api.World.Rand.NextDouble() * 0.4;

            NodesChecked = 0;

            PathNode startNode = new PathNode(start);
            PathNode targetNode = new PathNode(end);

            openSet.Clear();
            closedSet.Clear();

            openSet.Add(startNode);
            
            while (openSet.Count > 0)
            {
                if (NodesChecked++ > searchDepth)
                {
                    return null;
                }

                PathNode nearestNode = openSet.RemoveNearest();
                closedSet.Add(nearestNode);

                if (nearestNode == targetNode || (mhdistanceTolerance>0 && Math.Abs(nearestNode.X - targetNode.X) <= mhdistanceTolerance && Math.Abs(nearestNode.Z - targetNode.Z) <= mhdistanceTolerance && (Math.Abs(nearestNode.Y - targetNode.Y) <= mhdistanceTolerance /*|| (targetNode.Y > nearestNode.Y && targetNode.Y - nearestNode.Y < 4 + mhdistanceTolerance)* - why TF is this here? It makes path finds to pillard up players successfull! */)))
                {
                    return retracePath(startNode, nearestNode);
                }

                for (int i = 0; i < Cardinal.ALL.Length; i++)
                {
                    Cardinal card = Cardinal.ALL[i];

                    PathNode neighbourNode = new PathNode(nearestNode, card);

                    float extraCost = 0;
                    PathNode existingNeighbourNode = openSet.TryFindValue(neighbourNode);
                    if (!(existingNeighbourNode is null))   // we have to do a null check using "is null" due to foibles in PathNode.Equals()
                    {
                        // if it is already in openSet, update the gCost and parent if this nearestNode gives a shorter route to it
                        float baseCostToNeighbour = nearestNode.gCost + nearestNode.distanceTo(neighbourNode);
                        if (existingNeighbourNode.gCost > baseCostToNeighbour + 0.0001f)
                        {
                            if (traversable(neighbourNode, stepHeight, maxFallHeight, entityCollBox, card, ref extraCost) && existingNeighbourNode.gCost > baseCostToNeighbour + extraCost + 0.0001f)
                            {
                                UpdateNode(nearestNode, existingNeighbourNode, extraCost);
                            }
                        }
                    }
                    else if (!closedSet.Contains(neighbourNode))
                    {
                        if (traversable(neighbourNode, stepHeight, maxFallHeight, entityCollBox, card, ref extraCost))
                        {
                            UpdateNode(nearestNode, neighbourNode, extraCost);
                            
                            neighbourNode.hCost = neighbourNode.distanceTo(targetNode);
                            openSet.Add(neighbourNode);
                        }
                    }
                }
            }

            return null;
        }




        /// <summary>
        /// Actually now only sets fields in neighbourNode as appropriate.  The calling code must add this to openSet if necessary.
        /// </summary>
        protected void UpdateNode(PathNode nearestNode, PathNode neighbourNode, float extraCost)
        {
            neighbourNode.gCost = nearestNode.gCost + nearestNode.distanceTo(neighbourNode) + extraCost;
            neighbourNode.Parent = nearestNode;
            neighbourNode.pathLength = nearestNode.pathLength + 1;
        }


        [Obsolete("Deprecated, please use UpdateNode() instead")]
        protected void addIfNearer(PathNode nearestNode, PathNode neighbourNode, PathNode targetNode, HashSet<PathNode> openSet, float extraCost)
        {
            UpdateNode(nearestNode, neighbourNode, extraCost);
        }


        protected readonly Vec3d tmpVec = new Vec3d();
        protected readonly BlockPos tmpPos = new BlockPos();
        protected Cuboidd tmpCub = new Cuboidd();

        protected bool traversable(PathNode node, float stepHeight, int maxFallHeight, Cuboidf entityCollBox, Cardinal fromDir, ref float extraCost)
        {
            tmpVec.Set(node.X + centerOffsetX, node.Y, node.Z + centerOffsetZ);
            Block block;

            if (!collTester.IsColliding(blockAccess, entityCollBox, tmpVec, false))
            {
                int descended = 0;

                // Can I stand or jump down here?
                while (true)
                {
                    tmpPos.Set(node.X, node.Y - 1, node.Z);

                    block = blockAccess.GetBlock(tmpPos, BlockLayersAccess.Solid);
                    if (!block.CanStep) return false;

                    Block fluidLayerBlock = blockAccess.GetBlock(tmpPos, BlockLayersAccess.Fluid);
                    
                    if (fluidLayerBlock.IsLiquid())
                    {
                        var cost = fluidLayerBlock.GetTraversalCost(tmpPos, creatureType);             
                        if (cost > 10000) return false;
                        extraCost += cost;
                        //node.Y--; - we swim on top
                        break;
                    }
                    if (fluidLayerBlock.BlockMaterial == EnumBlockMaterial.Ice) block = fluidLayerBlock;

                    // Do we collide if we go one block down? 
                    // Our hitbox size might be >1 and we might collide with a wall only
                    // so also test if there is actually any collision box right below us. 

                    // If collision but no collision box below: Can't really wall-walk, so fail
                    // If no collision but hitbox below: I guess we can step on it to continue from here?
                    // If no collision and no hitbox below: Free fall, lets keep searching downwards

                    Cuboidf[] hitboxBelow = block.GetCollisionBoxes(blockAccess, tmpPos);
                    if (hitboxBelow != null && hitboxBelow.Length > 0)
                    {
                        var cost = block.GetTraversalCost(tmpPos, creatureType);
                        if (cost > 10000) return false;
                        extraCost += cost;
                        break;
                    }

                    // Only reduce the tmpVec if we are actually falling down 1 block
                    tmpVec.Y--;
                    if (collTester.IsColliding(blockAccess, entityCollBox, tmpVec, false))
                    {
                        // Can't wall walk
                        return false;
                    }

                    descended++;
                    node.Y--;
                    maxFallHeight--;

                    if (maxFallHeight < 0)
                    {
                        return false;
                    }
                }

                
                // Up ok?
                /*float height = entityCollBox.Height - descended;
                tmpVec.Y += descended;   // Do not re-check height in blocks we've already descended through
                while (--height > 0)
                {
                    tmpVec.Y++;
                    if (collTester.IsColliding(blockAccess, entityCollBox, tmpVec, false))
                    {
                        return false;
                    }
                }*/

                // If diagonal, make sure we can squeeze through
                if (fromDir.IsDiagnoal)
                {
                    tmpVec.Add(-fromDir.Normali.X / 2f, 0, -fromDir.Normali.Z / 2f);
                    if (collTester.IsColliding(blockAccess, entityCollBox, tmpVec, false))
                    {
                        return false;
                    }
                }

                // Don't walk into lava at the same height
                tmpPos.Set(node.X, node.Y, node.Z);
                var fblock = blockAccess.GetBlock(tmpPos, BlockLayersAccess.Fluid);
                var ucost = fblock.GetTraversalCost(tmpPos, creatureType);
                if (ucost > 10000) return false;
                extraCost += ucost;

                // Humans: Don't walk diagonally right besides lava
                if (fromDir.IsDiagnoal && creatureType == EnumAICreatureType.Humanoid)
                {
                    tmpPos.Set(node.X - fromDir.Normali.X, node.Y, node.Z);
                    fblock = blockAccess.GetBlock(tmpPos, BlockLayersAccess.Fluid);
                    ucost = fblock.GetTraversalCost(tmpPos, creatureType);
                    extraCost += ucost-1;
                    if (ucost > 10000) return false;

                    tmpPos.Set(node.X, node.Y, node.Z - fromDir.Normali.Z);
                    fblock = blockAccess.GetBlock(tmpPos, BlockLayersAccess.Fluid);
                    ucost = fblock.GetTraversalCost(tmpPos, creatureType);
                    extraCost += ucost-1;
                    if (ucost > 10000) return false;
                }

                return true;
            }
            
            tmpPos.Set(node.X, node.Y, node.Z);
            block = blockAccess.GetBlock(tmpPos, BlockLayersAccess.MostSolid);
            if (!block.CanStep) return false;
            float upcost = block.GetTraversalCost(tmpPos, creatureType);
            if (upcost > 10000) return false;
            if (block.Id != 0) extraCost += upcost;

            var lblock = blockAccess.GetBlock(tmpPos, BlockLayersAccess.Fluid);
            upcost = lblock.GetTraversalCost(tmpPos, creatureType);
            if (upcost > 10000) return false;
            if (lblock.Id != 0) extraCost += upcost;

            // Adjust "step on" height because not all blocks we are stepping onto are a full block high
            float steponHeightAdjust = -1f;
            Cuboidf[] collboxes = block.GetCollisionBoxes(blockAccess, tmpPos);
            if (collboxes != null && collboxes.Length > 0)
            {
                steponHeightAdjust += collboxes.Max((cuboid) => cuboid.Y2);
            }
            
            tmpVec.Set(node.X + centerOffsetX, node.Y + stepHeight + steponHeightAdjust, node.Z + centerOffsetZ);
            // Test for collision if we step up
            if (!collTester.GetCollidingCollisionBox(blockAccess, entityCollBox, tmpVec, ref tmpCub, false))
            {
                if (fromDir.IsDiagnoal)
                {
                    if (collboxes != null && collboxes.Length > 0)
                    {
                        // If diagonal, make sure we can squeeze through
                        tmpVec.Add(-fromDir.Normali.X / 2f, 0, -fromDir.Normali.Z / 2f);
                        if (collTester.IsColliding(blockAccess, entityCollBox, tmpVec, false))
                        {
                            return false;
                        }

                        // Ok, can step on this block
                        node.Y += (int)(1f + steponHeightAdjust);
                        return true;
                    }
                } else
                {
                    node.Y += (int)(1f + steponHeightAdjust);
                    return true;
                }
            }

            return false;
        }


        protected List<PathNode> retracePath(PathNode startNode, PathNode endNode)
        {
            int length = endNode.pathLength;
            List<PathNode> path = new List<PathNode>(length);
            for (int i = 0; i < length; i++) path.Add(null);  // pre-fill the path with dummy values to achieve the required Count, needed for assignment to path[i] later
            PathNode currentNode = endNode;

            for (int i = length - 1; i >=0; i--)
            {
                path[i] = currentNode;
                currentNode = currentNode.Parent;

                //var block = blockAccess.GetBlock(currentNode);
                //System.Diagnostics.Debug.WriteLine(block.Code + " - " + currentNode.gCost);
            }

            return path;
        }



        public List<Vec3d> ToWaypoints(List<PathNode> path)
        {
            List<Vec3d> waypoints = new List<Vec3d>(path.Count + 1);
            for (int i = 1; i < path.Count; i++)
            {
                waypoints.Add(path[i].ToWaypoint().Add(centerOffsetX, 0, centerOffsetZ));
            }

            // Some code that reduces the amount of waypoints needed for creature to follow. Not sure how useful it is plus it doesn't work well (creatures fall off ledges in some cases)
            /*Vec3i directionOld = Vec3i.Zero;
            Vec3i directionNew = new Vec3i();
            for (int i = 1; i < path.Count; i++)
            {
                directionNew.Set(path[i - 1].X - path[i].X, path[i - 1].Y - path[i].Y, path[i - 1].Z - path[i].Z);

                if (!directionNew.Equals(directionOld)) // || (i % 2 == 0))
                {
                    waypoints.Add(path[i].ToWaypoint());
                }
                directionOld = directionNew;
            }*/

            return waypoints;
        }


        public void Dispose()
        {
            blockAccess?.Dispose();
        }
    }
}
