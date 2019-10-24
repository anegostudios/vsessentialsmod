using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    public class AiTaskMeleeAttack : AiTaskBase, IWorldIntersectionSupplier
    {
        Entity targetEntity;

        long lastCheckOrAttackMs;

        float damage = 2f;
        float minDist = 1.5f;
        float minVerDist = 1f;


        bool damageInflicted = false;

        int attackDurationMs = 1500;
        int damagePlayerAtMs = 500;

        BlockSelection blockSel = new BlockSelection();
        EntitySelection entitySel = new EntitySelection();

        string[] seekEntityCodesExact = new string[] { "player" };
        string[] seekEntityCodesBeginsWith = new string[0];



        public Vec3i MapSize { get { return entity.World.BlockAccessor.MapSize; } }

        public AiTaskMeleeAttack(EntityAgent entity) : base(entity)
        {            
        }

        public override void LoadConfig(JsonObject taskConfig, JsonObject aiConfig)
        {
            base.LoadConfig(taskConfig, aiConfig);

            this.damage = taskConfig["damage"].AsFloat(2);
            this.attackDurationMs = taskConfig["attackDurationMs"].AsInt(1500);
            this.damagePlayerAtMs = taskConfig["damagePlayerAtMs"].AsInt(1000);

            this.minDist = taskConfig["minDist"].AsFloat(2f);
            this.minVerDist = taskConfig["minVerDist"].AsFloat(1f);

            if (taskConfig["entityCodes"] != null)
            {
                string[] codes = taskConfig["entityCodes"].AsArray<string>(new string[] { "player" });

                List<string> exact = new List<string>();
                List<string> beginswith = new List<string>();

                for (int i = 0; i < codes.Length; i++)
                {
                    string code = codes[i];
                    if (code.EndsWith("*")) beginswith.Add(code.Substring(0, code.Length - 1));
                    else exact.Add(code);
                }

                seekEntityCodesExact = exact.ToArray();
                seekEntityCodesBeginsWith = beginswith.ToArray();
            }
        }

        public override bool ShouldExecute()
        {
            long ellapsedMs = entity.World.ElapsedMilliseconds;
            if (ellapsedMs - lastCheckOrAttackMs < attackDurationMs || cooldownUntilMs > ellapsedMs)
            {
                return false;
            }
            if (whenInEmotionState != null && !entity.HasEmotionState(whenInEmotionState)) return false;
            if (whenNotInEmotionState != null && entity.HasEmotionState(whenNotInEmotionState)) return false;

            Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.CollisionBox.Y2 / 2, 0).Ahead((entity.CollisionBox.X2 - entity.CollisionBox.X1) / 2, 0, entity.ServerPos.Yaw);


            targetEntity = entity.World.GetNearestEntity(pos, 3f, 3f, (e) => {
                if (!e.Alive || !e.IsInteractable || e.EntityId == this.entity.EntityId) return false;

                for (int i = 0; i < seekEntityCodesExact.Length; i++)
                {
                    if (e.Code.Path == seekEntityCodesExact[i])
                    {
                        if (e.Code.Path == "player")
                        {
                            IPlayer player = entity.World.PlayerByUid(((EntityPlayer)e).PlayerUID);
                            bool okplayer =
                                player == null ||
                                (player.WorldData.CurrentGameMode != EnumGameMode.Creative && player.WorldData.CurrentGameMode != EnumGameMode.Spectator && (player as IServerPlayer).ConnectionState == EnumClientState.Playing);

                            return okplayer && hasDirectContact(e);
                        }

                        if (hasDirectContact(e))
                        {
                            return true;
                        }
                    }
                }


                for (int i = 0; i < seekEntityCodesBeginsWith.Length; i++)
                {
                    if (e.Code.Path.StartsWith(seekEntityCodesBeginsWith[i]) && hasDirectContact(e))
                    {
                        return true;
                    }
                }

                return false;
            });

            lastCheckOrAttackMs = entity.World.ElapsedMilliseconds;
            damageInflicted = false;

            return targetEntity != null;
        }



        public override bool ContinueExecute(float dt)
        {
            EntityPos own = entity.ServerPos;
            EntityPos his = targetEntity.ServerPos;
            entity.ServerPos.Yaw = (float)(float)Math.Atan2(his.X - own.X, his.Z - own.Z);

            if (lastCheckOrAttackMs + damagePlayerAtMs > entity.World.ElapsedMilliseconds) return true;

            if (!damageInflicted)
            {
                if (!hasDirectContact(targetEntity)) return false;

                bool alive = targetEntity.Alive;
                
                ((EntityAgent)targetEntity).ReceiveDamage(
                    new DamageSource() { Source = EnumDamageSource.Entity, SourceEntity = entity, Type = EnumDamageType.BluntAttack },
                    damage * GlobalConstants.CreatureDamageModifier
                );

                if (alive && !targetEntity.Alive)
                {
                    this.entity.GetBehavior<EntityBehaviorEmotionStates>()?.TryTriggerState("saturated");
                }

                damageInflicted = true;
            }

            if (lastCheckOrAttackMs + attackDurationMs > entity.World.ElapsedMilliseconds) return true;
            return false;
        }



        bool hasDirectContact(Entity targetEntity)
        {
            Cuboidd targetBox = targetEntity.CollisionBox.ToDouble().Translate(targetEntity.ServerPos.X, targetEntity.ServerPos.Y, targetEntity.ServerPos.Z);
            Vec3d pos = entity.ServerPos.XYZ.Add(0, entity.CollisionBox.Y2 / 2, 0).Ahead((entity.CollisionBox.X2 - entity.CollisionBox.X1) / 2, 0, entity.ServerPos.Yaw);
            double dist = targetBox.ShortestDistanceFrom(pos);
            double vertDist = Math.Abs(targetBox.ShortestVerticalDistanceFrom(pos.Y));
            if (dist >= minDist || vertDist >= minVerDist) return false;

            Vec3d rayTraceFrom = entity.ServerPos.XYZ;
            rayTraceFrom.Y += 1 / 32f;
            Vec3d rayTraceTo = targetEntity.ServerPos.XYZ;
            rayTraceTo.Y += 1 / 32f;
            bool directContact = false;

            entity.World.RayTraceForSelection(this, rayTraceFrom, rayTraceTo, ref blockSel, ref entitySel);
            directContact = blockSel == null;

            if (!directContact)
            {
                rayTraceFrom.Y += entity.CollisionBox.Y2 * 7 / 16f;
                rayTraceTo.Y += targetEntity.CollisionBox.Y2 * 7 / 16f;
                entity.World.RayTraceForSelection(this, rayTraceFrom, rayTraceTo, ref blockSel, ref entitySel);
                directContact = blockSel == null;
            }

            if (!directContact)
            {
                rayTraceFrom.Y += entity.CollisionBox.Y2 * 7 / 16f;
                rayTraceTo.Y += targetEntity.CollisionBox.Y2 * 7 / 16f;
                entity.World.RayTraceForSelection(this, rayTraceFrom, rayTraceTo, ref blockSel, ref entitySel);
                directContact = blockSel == null;
            }

            if (!directContact) return false;

            return true;
        }


        public Block GetBlock(BlockPos pos)
        {
            return entity.World.BlockAccessor.GetBlock(pos);
        }

        public Cuboidf[] GetBlockIntersectionBoxes(BlockPos pos)
        {
            return entity.World.BlockAccessor.GetBlock(pos).GetCollisionBoxes(entity.World.BlockAccessor, pos);
        }

        public bool IsValidPos(BlockPos pos)
        {
            return entity.World.BlockAccessor.IsValidPos(pos);
        }


        public Entity[] GetEntitiesAround(Vec3d position, float horRange, float vertRange, ActionConsumable<Entity> matches = null)
        {
            return new Entity[0];
        }
    }
}