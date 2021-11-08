using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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

        public AStar(ICoreServerAPI api)
        {
            this.api = api;
            blockAccess = api.World.GetCachingBlockAccessor(true, true);
        }

        public PathNodeSet openSet = new PathNodeSet();
        public HashSet<PathNode> closedSet = new HashSet<PathNode>();

        public List<Vec3d> FindPathAsWaypoints(BlockPos start, BlockPos end, int maxFallHeight, float stepHeight, Cuboidf entityCollBox, int searchDepth = 9999, bool allowReachAlmost = false)
        {
            List<PathNode> nodes = FindPath(start, end, maxFallHeight, stepHeight, entityCollBox, searchDepth, allowReachAlmost);
            return nodes == null ? null : ToWaypoints(nodes);
        }

        public List<PathNode> FindPath(BlockPos start, BlockPos end, int maxFallHeight, float stepHeight, Cuboidf entityCollBox, int searchDepth = 9999, bool allowReachAlmost = false)
        {
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
                if (NodesChecked++ > searchDepth) return null;

                PathNode nearestNode = openSet.RemoveNearest();
                closedSet.Add(nearestNode);

                if (nearestNode == targetNode || (allowReachAlmost && Math.Abs(nearestNode.X - targetNode.X) <= 1 && Math.Abs(nearestNode.Z - targetNode.Z) <= 1 && (nearestNode.Y == targetNode.Y || nearestNode.Y == targetNode.Y + 1)))
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
                            if (traversable(neighbourNode, stepHeight, maxFallHeight, entityCollBox, card.IsDiagnoal, ref extraCost) && existingNeighbourNode.gCost > baseCostToNeighbour + extraCost + 0.0001f)
                            {
                                UpdateNode(nearestNode, existingNeighbourNode, extraCost);
                            }
                        }
                    }
                    else if (!closedSet.Contains(neighbourNode))
                    {
                        if (traversable(neighbourNode, stepHeight, maxFallHeight, entityCollBox, card.IsDiagnoal, ref extraCost))
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
        private void UpdateNode(PathNode nearestNode, PathNode neighbourNode, float extraCost)
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


        Vec3d tmpVec = new Vec3d();
        BlockPos tmpPos = new BlockPos();
        Cuboidd tmpCub = new Cuboidd();

        protected bool traversable(PathNode node, float stepHeight, int maxFallHeight, Cuboidf entityCollBox, bool isDiagonal, ref float extraCost)
        {
            tmpVec.Set(node.X + centerOffsetX, node.Y, node.Z + centerOffsetZ);

            if (!api.World.CollisionTester.IsColliding(blockAccess, entityCollBox, tmpVec, false))
            {
                int descended = 0;

                // Down ok?
                while (true)
                {
                    tmpPos.Set(node.X, node.Y - 1, node.Z);

                    Block block = blockAccess.GetBlock(tmpPos);
                    if (block.LiquidCode == "lava" || !block.CanStep) return false; // TODO: Turn into an entityAvoid boolean

                    if (block.LiquidCode == "water")
                    {
                        extraCost = 5;
                        //node.Y--; - we swim on top
                        break;
                    }

                    // Do we collide if we go one block down? 
                    // Our hitbox size might be >1 and we might collide with a wall only
                    // so also test if there is actually any collision box right below us. 

                    // If collision but no collision box below: Can't really wall-walk, so fail
                    // If no collision but hitbox below: I guess we can step on it to continue from here?
                    // If no collision and no hitbox below: Free fall, lets keep searching downwards

                    Cuboidf[] hitboxBelow = block.GetCollisionBoxes(blockAccess, tmpPos);
                    if (hitboxBelow != null && hitboxBelow.Length > 0)
                    {
                        // Cheap exit from the while() loop in the most common case: there is a block below
                        extraCost -= (block.WalkSpeedMultiplier - 1) * 8;
                        break;
                    }

                    // Only reduce the tmpVec if we are actually falling down 1 block
                    tmpVec.Y--;
                    if (api.World.CollisionTester.IsColliding(blockAccess, entityCollBox, tmpVec, false))
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
                float height = entityCollBox.Height - descended;
                tmpVec.Y += descended;   // Do not re-check height in blocks we've already descended through
                while (--height > 0)
                {
                    tmpVec.Y++;
                    if (api.World.CollisionTester.IsColliding(blockAccess, entityCollBox, tmpVec, false))
                    {
                        return false;
                    }
                }

                return true;
            }
            else
            {
                tmpPos.Set(node.X, node.Y, node.Z);
                Block block = blockAccess.GetBlock(tmpPos);
                if (!block.CanStep) return false;

                tmpVec.Set(node.X + centerOffsetX, node.Y + stepHeight, node.Z + centerOffsetZ);
                // Test for collision if we step up
                if (!api.World.CollisionTester.GetCollidingCollisionBox(blockAccess, entityCollBox, tmpVec, ref tmpCub, false))
                {
                    if (isDiagonal)
                    {
                        Cuboidf[] collboxes = block.GetCollisionBoxes(blockAccess, tmpPos);

                        if (collboxes != null && collboxes.Length > 0)
                        {
                            // Ok, can step on this block
                            node.Y++;
                            return true;
                        }
                    } else
                    {
                        node.Y++;
                        return true;
                    }
                }
            }


            return false;
        }


        List<PathNode> retracePath(PathNode startNode, PathNode endNode)
        {
            int length = endNode.pathLength;
            List<PathNode> path = new List<PathNode>(length);
            for (int i = 0; i < length; i++) path.Add(null);  // pre-fill the path with dummy values to achieve the required Count, needed for assignment to path[i] later
            PathNode currentNode = endNode;

            for (int i = length - 1; i >=0; i--)
            {
                path[i] = currentNode;
                currentNode = currentNode.Parent;
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

    }
}
