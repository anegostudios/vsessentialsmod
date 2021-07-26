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

        public HashSet<PathNode> openSet = new HashSet<PathNode>();
        public HashSet<PathNode> closedSet = new HashSet<PathNode>();

        public List<Vec3d> FindPathAsWaypoints(BlockPos start, BlockPos end, int maxFallHeight, float stepHeight, Cuboidf entityCollBox, int searchDepth = 9999, bool allowReachAlmost = false)
        {
            List<PathNode> nodes = FindPath(start, end, maxFallHeight, stepHeight, entityCollBox, searchDepth, allowReachAlmost);
            return nodes == null ? null : ToWaypoints(nodes);
        }

        public List<PathNode> FindPath(BlockPos start, BlockPos end, int maxFallHeight, float stepHeight, Cuboidf entityCollBox, int searchDepth = 9999, bool allowReachAlmost = false)
        {
            blockAccess.Begin();

            centerOffsetX = 0.35 + api.World.Rand.NextDouble() * 0.35;
            centerOffsetZ = 0.35 + api.World.Rand.NextDouble() * 0.35;

            NodesChecked = 0;

            PathNode startNode = new PathNode(start);
            PathNode targetNode = new PathNode(end);

            openSet.Clear();
            closedSet.Clear();

            openSet.Add(startNode);
            
            while (openSet.Count > 0)
            {
                if (NodesChecked++ > searchDepth) return null;

                PathNode nearestNode = openSet.First();
                foreach (var node in openSet)
                {
                    if (node.fCost <= nearestNode.fCost && node.hCost < nearestNode.hCost)
                    {
                        nearestNode = node;
                    }
                }

                openSet.Remove(nearestNode);
                closedSet.Add(nearestNode);

                //Console.WriteLine(string.Format("Distance: {0}/{1}/{2}", nearestNode.X - targetNode.X, nearestNode.Y - targetNode.Y, nearestNode.Z - targetNode.Z));

                if (nearestNode == targetNode || (allowReachAlmost && Math.Abs(nearestNode.X - targetNode.X) <= 1 && Math.Abs(nearestNode.Z - targetNode.Z) <= 1 && (nearestNode.Y == targetNode.Y || nearestNode.Y == targetNode.Y + 1)))
                {
                    return retracePath(startNode, nearestNode);
                }

                for (int i = 0; i < Cardinal.ALL.Length; i++)
                {
                    Cardinal card = Cardinal.ALL[i];

                    PathNode neighbourNode = new PathNode(nearestNode, card);

                    float extraCost = 0;
                    if (traversable(neighbourNode, stepHeight, maxFallHeight, entityCollBox, card.IsDiagnoal, ref extraCost) && !closedSet.Contains(neighbourNode))
                    {
                        addIfNearer(nearestNode, neighbourNode, targetNode, openSet, extraCost);
                    }
                }
            }

            return null;
        }

        



        protected void addIfNearer(PathNode nearestNode, PathNode neighbourNode, PathNode targetNode, HashSet<PathNode> openSet, float extraCost)
        {
            float newCostToNeighbour = nearestNode.gCost + nearestNode.distanceTo(neighbourNode) + extraCost;

            if ((newCostToNeighbour < neighbourNode.gCost) || !openSet.Contains(neighbourNode))
            {
                neighbourNode.gCost = newCostToNeighbour;
                neighbourNode.hCost = neighbourNode.distanceTo(targetNode);
                neighbourNode.Parent = nearestNode;

                openSet.Add(neighbourNode);
            }
        }


        Vec3d tmpVec = new Vec3d();
        BlockPos tmpPos = new BlockPos();
        Cuboidd tmpCub = new Cuboidd();

        protected bool traversable(PathNode node, float stepHeight, int maxFallHeight, Cuboidf entityCollBox, bool isDiagonal, ref float extraCost)
        {
            tmpVec.Set(node.X + centerOffsetX, node.Y, node.Z + centerOffsetZ);

            if (!api.World.CollisionTester.IsColliding(blockAccess, entityCollBox, tmpVec, false))
            {
                // Down ok?
                while (true)
                {
                    tmpPos.Set(node.X, node.Y - 1, node.Z);

                    Block block = blockAccess.GetBlock(tmpPos);
                    if (block.LiquidCode == "lava") return false; // TODO: Turn into an entityAvoid boolean

                    if (block.LiquidCode == "water")
                    {
                        extraCost = 5;
                        //node.Y--; - we swim on top
                        break;
                    }

                    tmpVec.Y--;

                    // Do we collide if we go one block down? 
                    // Our hitbox size might be >1 and we might collide with a wall only
                    // so also test if there is actually any collision box right below us. 

                    // If collision but no collision box below: Can't really wall-walk, so fail
                    // If no collision but hitbox below: I guess we can step on it to continue from here?
                    // If no collision and no hitbox below: Free fall, lets keep searching downwards

                    Cuboidf[] collboxes = block.GetCollisionBoxes(blockAccess, tmpPos);
                    bool collidingBlockBelow = collboxes != null && collboxes.Length > 0;

                    if (api.World.CollisionTester.IsColliding(blockAccess, entityCollBox, tmpVec, false))
                    {
                        if (!collidingBlockBelow)
                        {
                            return false;
                        }

                        extraCost -= (block.WalkSpeedMultiplier - 1) * 8;
                        break;
                    } else
                    {
                        if (collidingBlockBelow)
                        {
                            extraCost -= (block.WalkSpeedMultiplier - 1) * 8;
                            break;
                        }
                    }

                    
                    node.Y--;
                    maxFallHeight--;

                    if (maxFallHeight < 0)
                    {
                        return false;
                    }
                }

                // Up ok?
                float height = entityCollBox.Height;
                while (height-- > 0)
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
                tmpVec.Set(node.X + centerOffsetX, node.Y + stepHeight, node.Z + centerOffsetZ);
                bool collideAbove = api.World.CollisionTester.GetCollidingCollisionBox(blockAccess, entityCollBox, tmpVec, ref tmpCub, false);

                tmpPos.Set(node.X, node.Y, node.Z);
                Block block = blockAccess.GetBlock(tmpPos);
                if (!block.CanStep) return false;

                if (!collideAbove)
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
            List<PathNode> path = new List<PathNode>();
            PathNode currentNode = endNode;

            while (currentNode != startNode)
            {
                path.Add(currentNode);
                currentNode = currentNode.Parent;
            }
            path.Reverse();

            return path;
        }



        public List<Vec3d> ToWaypoints(List<PathNode> path)
        {
            List<Vec3d> waypoints = new List<Vec3d>(path.Count + 1);
            for (int i = 1; i < path.Count; i++)
            {
                waypoints.Add(path[i].ToWaypoint().Add(centerOffsetX, 0, centerOffsetZ));
            }

            // Some code that reduces the amount of waypoints needed for creature to follow. Not sure how useful it is plus it doesn't work well (creatures fall of ledges in some cases)
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
