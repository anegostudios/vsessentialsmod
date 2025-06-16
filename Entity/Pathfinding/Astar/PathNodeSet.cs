using System.Collections;
using System.Collections.Generic;

#nullable disable

namespace Vintagestory.Essentials
{
    /// <summary>
    /// A fast custom set of type PathNode, for use in the AStar algorithm
    /// </summary>
    public class PathNodeSet : IEnumerable<PathNode>
    {
        int arraySize = 16;
        PathNode[][] buckets = new PathNode[4][];
        int[] bucketCount = new int[4];
        int size = 0;

        public int Count {
            get {
                return size;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < 4; i++) bucketCount[i] = 0;
            size = 0;
        }

        public PathNodeSet()
        {
            for (int i = 0; i < 4; i++) buckets[i] = new PathNode[arraySize];
        }

        /// <summary>
        /// Return false if the set already contained this value; return true if the Add was successful
        /// </summary>
        public bool Add(PathNode value)
        {
            int bucket = (value.Z % 2) * 2 + (value.X % 2);
            bucket = (bucket + 4) % 4;    // we cannot assume X and Z are both positive, could be near map edge
            PathNode[] set = buckets[bucket];

            // fast search, start from the most recently added
            int size = bucketCount[bucket];
            int i = size;
            while (--i >= 0)
            {
                if (value.Equals(set[i])) return false;
            }

            // now actually add the value
            if (size >= arraySize)
            {
                ExpandArrays();
                set = buckets[bucket];  // re-establish the set because buckets[bucket] will have been changed to a new object by ExpandArrays()
            }
            // find the set element i with the next higher cost...
            float fCost = value.fCost;
            for (i = size - 1; i >= 0; i--)
            {
                if (set[i].fCost < fCost) continue;
                if (set[i].fCost == fCost && set[i].hCost < value.hCost) continue;   // compare the h values as a tie-breaker; if still a tie, newest will be last
                break;
            }
            // ...and insert this value just afterwards so that lowest cost is always last in the set.
            i++;
            int j = size;
            while (j > i) set[j] = set[--j];
            set[i] = value;
            size++;
            bucketCount[bucket] = size;
            this.size++;
            return true;
        }

        /// <summary>
        /// Return the lowest fCost PathNode, removing it from the set as well
        /// </summary>
        public PathNode RemoveNearest()
        {
            if (this.size == 0) return null;
            PathNode nearestNode = null;
            int bucketToRemoveFrom = 0;

            // The last PathNode in each bucket should always have the lowest fCost (this is achieved by the Add() method)
            // So we only have to compare (maximum) four values to find the PathNode with the absolute lowest fCost
            for (int bucket = 0; bucket < 4; bucket++)
            {
                int endIndex = bucketCount[bucket] - 1;
                if (endIndex < 0) continue;
                PathNode node = buckets[bucket][endIndex];

                if (nearestNode is null || node.fCost < nearestNode.fCost || (node.fCost == nearestNode.fCost && node.hCost < nearestNode.hCost))  // we have to do the null check this way around due to foibles of PathNode.Equals()
                {
                    nearestNode = node;
                    bucketToRemoveFrom = bucket;
                }
            }

            // now actually remove the value - easy because it is always the last one in its bucket
            --bucketCount[bucketToRemoveFrom];
            this.size--;
            return nearestNode;
        }


        public void Remove(PathNode value)
        {
            int bucket = (value.Z % 2) * 2 + (value.X % 2);
            bucket = (bucket + 4) % 4;    // we cannot assume X and Z are both positive, could be near map edge
            PathNode[] set = buckets[bucket];

            // fast search, start from the most recently added
            int size = bucketCount[bucket];
            int i = size;
            while (--i >= 0)
            {
                if (value.Equals(set[i]))
                {
                    // now actually remove the value
                    size = --bucketCount[bucket];
                    while (i < size) set[i] = set[++i];
                    this.size--;

                    break;
                }
            }
        }

        public PathNode TryFindValue(PathNode value)
        {
            int bucket = (value.Z % 2) * 2 + (value.X % 2);
            bucket = (bucket + 4) % 4;    // we cannot assume X and Z are both positive, could be near map edge
            PathNode[] set = buckets[bucket];

            // fast search, start from the most recently added
            int size = bucketCount[bucket];
            int i = size;
            while (--i >= 0)
            {
                if (value.Equals(set[i])) return set[i];
            }

            return null;
        }


        /// <summary>
        /// Expand the array in all buckets - not much point in having buckets of different sizes, this class doesn't take a huge amount of memory anyhow
        /// </summary>
        private void ExpandArrays()
        {
            int newSize = arraySize * 3 / 2;
            for (int bucket = 0; bucket < 4; bucket++)
            {
                PathNode[] newArray = new PathNode[newSize];
                int size = bucketCount[bucket];
                PathNode[] set = buckets[bucket];
                for (int i = 0; i < size; i++) newArray[i] = set[i];
                buckets[bucket] = newArray;
            }
            arraySize = newSize;
        }


        public IEnumerator<PathNode> GetEnumerator()
        {
            for (int bucket = 0; bucket < 4; bucket++)
                for (int i = 0; i < bucketCount[bucket]; i++)
                    yield return buckets[bucket][i];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
