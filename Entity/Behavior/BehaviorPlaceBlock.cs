using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorPlaceBlock : EntityBehavior
    {
        public override string PropertyName()
        {
            return "createblock";
        }

        ITreeAttribute createBlockTree;
        JsonObject attributes;
        long callbackId;

        internal float MinHourDelay
        {
            get { return attributes["minHourDelay"].AsFloat(8 * 24); }
        }

        internal float MaxHourDelay
        {
            get { return attributes["maxHourDelay"].AsFloat(15 * 24); }
        }

        internal float RndHourDelay
        {
            get
            {
                float min = MinHourDelay;
                float max = MaxHourDelay;
                return min + (float)entity.World.Rand.NextDouble() * (max - min);
            }
        }

        internal AssetLocation[] BlockCodes
        {
            get {
                string[] codes = attributes["blockCodes"].AsStringArray(new string[0]);
                AssetLocation[] locs = new AssetLocation[codes.Length];
                for (int i = 0; i < locs.Length; i++) locs[i] = new AssetLocation(codes[i]);
                return locs;
            }
        }
        


        internal double TotalHoursUntilPlace
        {
            get { return createBlockTree.GetDouble("TotalHoursUntilPlace"); }
            set { createBlockTree.SetDouble("TotalHoursUntilPlace", value); }
        }


        public EntityBehaviorPlaceBlock(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            this.attributes = attributes;

            createBlockTree = entity.WatchedAttributes.GetTreeAttribute("behaviorCreateBlock");

            if (createBlockTree == null)
            {
                entity.WatchedAttributes.SetAttribute("behaviorCreateBlock", createBlockTree = new TreeAttribute());
                TotalHoursUntilPlace = entity.World.Calendar.TotalHours + RndHourDelay;
            }

            callbackId = entity.World.RegisterCallback(CheckShouldPlace, 3000);
        }

        private void CheckShouldPlace(float dt)
        {
            if (!entity.Alive) return;

            callbackId = entity.World.RegisterCallback(CheckShouldPlace, 3000);

            if (entity.World.Calendar == null) return;
            
            if (entity.World.Calendar.TotalHours > TotalHoursUntilPlace)
            {
                AssetLocation[] codes = BlockCodes;
                Block block = entity.World.GetBlock(codes[entity.World.Rand.Next(codes.Length)]);
                if (block == null) return;

                bool placed =
                    TryPlace(block, 0, 0, 0) ||
                    TryPlace(block, 1, 0, 0) ||
                    TryPlace(block, 0, 0, -1) ||
                    TryPlace(block, -1, 0, 0) ||
                    TryPlace(block, 0, 0, 1)
                ;

                if (placed) TotalHoursUntilPlace = entity.World.Calendar.TotalHours + RndHourDelay;
            }

            entity.World.FrameProfiler.Mark("entity-creatblock");
        }

        private bool TryPlace(Block block, int dx, int dy, int dz)
        {
            IBlockAccessor blockAccess = entity.World.BlockAccessor;
            BlockPos pos = entity.ServerPos.XYZ.AsBlockPos.Add(dx, dy, dz);
            Block blockAtPos = blockAccess.GetBlock(pos);

            if (blockAtPos.IsReplacableBy(block) && blockAccess.GetBlock(pos.X, pos.Y - 1, pos.Z).SideSolid[BlockFacing.UP.Index])
            {
                blockAccess.SetBlock(block.BlockId, pos);
                return true;
            }

            return false;
        }

        public override void OnEntityDespawn(EntityDespawnReason despawn)
        {
            entity.World.UnregisterCallback(callbackId);
        }

        
    }
}
