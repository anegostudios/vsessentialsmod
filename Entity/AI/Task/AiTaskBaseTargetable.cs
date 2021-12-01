using System;
using System.Collections.Generic;
using System.Threading;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public abstract class AiTaskBaseTargetable : AiTaskBase
    {
        protected HashSet<string> targetEntityCodesExact = new HashSet<string>();
        protected string[] targetEntityCodesBeginsWith = new string[0];

        protected AiTaskBaseTargetable(EntityAgent entity) : base(entity)
        {
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            if (taskConfig["entityCodes"] != null)
            {
                string[] codes = taskConfig["entityCodes"].AsArray<string>(new string[] { "player" });

                List<string> beginswith = new List<string>();

                for (int i = 0; i < codes.Length; i++)
                {
                    string code = codes[i];
                    if (code.EndsWith("*")) beginswith.Add(code.Substring(0, code.Length - 1));
                    else targetEntityCodesExact.Add(code);
                }
                
                targetEntityCodesBeginsWith = beginswith.ToArray();
            }
        }


        public bool isTargetableEntity(Entity e, float range)
        {
            if (!e.Alive || !e.IsInteractable || e.EntityId == entity.EntityId || !CanSense(e, range)) return false;

            if (targetEntityCodesExact.Contains(e.Code.Path)) return true;

            for (int i = 0; i < targetEntityCodesBeginsWith.Length; i++)
            {
                if (e.Code.Path.StartsWithFast(targetEntityCodesBeginsWith[i])) return true;
            }

            return false;
        }


        public bool CanSense(Entity e, double range)
        {
            if (e is EntityPlayer eplr)
            {
                float rangeMul = e.Stats.GetBlended("animalSeekingRange");
                IPlayer player = eplr.Player;

                // Sneaking reduces the detection range
                if (eplr.Controls.Sneak && eplr.OnGround)
                {
                    rangeMul *= 0.6f;
                }

                return
                    (rangeMul == 1 || entity.ServerPos.DistanceTo(e.Pos.XYZ) < range * rangeMul) &&
                    (player == null || (player.WorldData.CurrentGameMode != EnumGameMode.Creative && player.WorldData.CurrentGameMode != EnumGameMode.Spectator && (player as IServerPlayer).ConnectionState == EnumClientState.Playing))
                ;
            }

            return true;
        }

    }
}
