using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Vintagestory.GameContent
{
    public struct JsonGeneGroup
    {
        public string Name;
        public int Count;
    }

    [ProtoContract]
    public struct NameGroupMapping
    {
        [ProtoMember(1)]
        public readonly string[] GroupNames;
        [ProtoMember(2)]
        public readonly int[] GroupSizes;

        public int GeneCount { get => GroupSizes.Sum(); }

        public int GeneGroupCount { get => GroupSizes.Length; }

        public int GroupOrdinal(string name)
        {
            for (int i = 0; i < GroupNames.Length; ++i)
            {
                if (GroupNames[i] == name)
                {
                    return i;
                }
            }
            throw new ArgumentException(name + " was not found among gene group names");
        }

        public int GroupSize(string name)
        {
            return GroupSizes[GroupOrdinal(name)];
        }

        public Range GeneRange(string name)
        {
            Range result = TryGetRange(name);
            if (result.End.Value == 0)
            {
                throw new ArgumentException(name + " was not found among gene group names");
            }
            return (Range)result;
        }

        public Range TryGetRange(string name)
        {
            int start = 0;
            for (int i = 0; i < GroupNames.Length; ++i)
            {
                if (GroupNames[i] == name)
                {
                    int end = start + GroupSizes[i];
                    return start..end;
                }
                start += GroupSizes[i];
            }
            return 0..0;
        }

        public NameGroupMapping()
        {
            GroupNames = new string[0];
            GroupSizes = new int[0];
        }

        public NameGroupMapping(string[] groupNames, int[] groupSizes)
        {
            if (groupNames.Length != groupSizes.Length)
            {
                throw new ArgumentException($"Cannot create name group mapping with inconsistent number of total groups. Names: {groupNames.Length}, Sizes: {groupSizes.Length}");
            }
            GroupNames = groupNames;
            GroupSizes = groupSizes;
        }

        public NameGroupMapping(JsonGeneGroup[]? groups)
        {
            if (groups == null)
            {
                GroupNames = new string[0];
                GroupSizes = new int[0];
                return;
            }

            GroupNames = new string[groups.Length];
            GroupSizes = new int[groups.Length];
            for (int i = 0; i < groups.Length; ++i)
            {
                GroupNames[i] = groups[i].Name;
                if (groups[i].Count < 0)
                {
                    throw new Exception(groups[i].Name + ": Can't have a negative number of genes! count=" + groups[i].Count);
                }
                GroupSizes[i] = groups[i].Count;
            }
        }
    }
}
