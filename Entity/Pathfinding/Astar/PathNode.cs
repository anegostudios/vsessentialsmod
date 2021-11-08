using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;

namespace Vintagestory.Essentials
{
    public enum EnumTraverseAction
    {
        Walk,
        Climb,
    }

    public class PathNode : BlockPos, IEquatable<PathNode>
    {
        // Actual cost until this node
        public float gCost;

        // Heuristic cost until target
        public float hCost;

        // Total cost from start to end
        public float fCost => gCost + hCost;

        public int HeapIndex { get; set; }

        public PathNode Parent;
        public int pathLength = 0;
        public EnumTraverseAction Action;
        

        public PathNode(PathNode nearestNode, Cardinal card) : base (nearestNode.X + card.Normali.X, nearestNode.Y + card.Normali.Y, nearestNode.Z + card.Normali.Z)
        { 
        }

        public PathNode(BlockPos pos) : base (pos.X, pos.Y, pos.Z)
        {
            
        }

        public bool Equals(PathNode other)
        {
            return other.X == X && other.Y == Y && other.Z == Z;
        }

        public override bool Equals(object obj)
        {
            return obj is PathNode && Equals(obj as PathNode);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public static bool operator ==(PathNode left, PathNode right)
        {
            if (object.ReferenceEquals(left, null))
            {
                return object.ReferenceEquals(right, null);
            }

            return left.Equals(right);
        }

        public static bool operator !=(PathNode left, PathNode right)
        {
            return !(left == right);
        }

        public float distanceTo(PathNode node)
        {
            /*int dstX = Math.Abs(node.X - X);
            int dstZ = Math.Abs(node.Z - Z);

            if (dstX > dstZ)
            {
                return 14 * dstZ + 10 * (dstX - dstZ);
            }
            else
            {
                return 14 * dstX + 10 * (dstZ - dstX);
            }*/

            
            int dx = Math.Abs(node.X - X);
            int dz = Math.Abs(node.Z - Z);

            // Use the diagonal distance, i.e. every step must be straight or a diagonal
            return dx > dz ? dx - dz + 1.4142136f * dz : dz - dx + 1.4142136f * dx; // GameMath.Sqrt(dx * dx + dz * dz);
        }

        public Vec3d ToWaypoint()
        {
            return new Vec3d(X, Y, Z);
        }

        public int CompareTo(PathNode other)
        {
            int compare = fCost.CompareTo(other.fCost);
            if (compare == 0)
            {
                compare = hCost.CompareTo(other.hCost);
            }
            return -compare;
        }
    }


}
