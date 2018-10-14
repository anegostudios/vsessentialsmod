using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;

namespace Vintagestory.GameContent
{
    public class AiTaskIdle : AiTaskBase
    {
        public AiTaskIdle(EntityAgent entity) : base(entity)
        {
        }

        public int minduration;
        public int maxduration;
        public float chance;
        public AssetLocation onBlockBelowCode;

        public long idleUntilMs;

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            this.minduration = taskConfig["minduration"].AsInt(2000);
            this.maxduration = taskConfig["maxduration"].AsInt(4000);
            this.chance = taskConfig["chance"].AsFloat(1.1f);
            string code = taskConfig["onBlockBelowCode"].AsString(null);
            if (code != null && code.Length > 0)
            {
                this.onBlockBelowCode = new AssetLocation(code);
            }

            idleUntilMs = entity.World.ElapsedMilliseconds + minduration + entity.World.Rand.Next(maxduration - minduration);

            base.LoadConfig(taskConfig, aiConfig);
        }

        public override bool ShouldExecute()
        {
            if (entity.World.Rand.NextDouble() < chance && cooldownUntilMs < entity.World.ElapsedMilliseconds)
            {
                if (onBlockBelowCode == null) return true;
                Block block = entity.World.BlockAccessor.GetBlock((int)entity.ServerPos.X, (int)entity.ServerPos.Y, (int)entity.ServerPos.Z);
                Block belowBlock = entity.World.BlockAccessor.GetBlock((int)entity.ServerPos.X, (int)entity.ServerPos.Y - 1, (int)entity.ServerPos.Z);
                return block.WildCardMatch(onBlockBelowCode) || (belowBlock.WildCardMatch(onBlockBelowCode) && block.Replaceable >= 6000);
            }
            return false;
        }

        public override void StartExecute()
        {
            base.StartExecute();
            idleUntilMs = entity.World.ElapsedMilliseconds + minduration + entity.World.Rand.Next(maxduration - minduration);
        }

        public override bool ContinueExecute(float dt)
        {
            return entity.World.ElapsedMilliseconds < idleUntilMs;
        }
        
    }
}
