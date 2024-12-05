using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorBreathe : EntityBehavior
    {
        ITreeAttribute oxygenTree;

        private float oxygenCached = -1f;
        public float Oxygen
        {
            get { return oxygenCached = oxygenTree.GetFloat("currentoxygen"); }
            set {
                if (value != oxygenCached)
                {
                    oxygenCached = value;
                    oxygenTree.SetFloat("currentoxygen", value);
                    entity.WatchedAttributes.MarkPathDirty("oxygen");
                }
                
            }
        }

        private float maxOxygen;
        public float MaxOxygen
        {
            get { return maxOxygen; }
            set { maxOxygen = value; oxygenTree.SetFloat("maxoxygen", value); entity.WatchedAttributes.MarkPathDirty("oxygen"); }
        }

        public bool HasAir
        {
            get { return oxygenTree.GetBool("hasair"); }
            set {
                bool prevValue = oxygenTree.GetBool("hasair");
                if (prevValue != value)
                {
                    oxygenTree.SetBool("hasair", value);
                    entity.WatchedAttributes.MarkPathDirty("oxygen");
                }
            }
        }

        // The padding that the collisionbox is adjusted by for suffocation damage.  Can be adjusted as necessary - don't set to exactly 0.
        Cuboidd tmp = new Cuboidd();
        float breathAccum = 0;
        float padding = 0.1f;
        Block suffocationSourceBlock;
        float damageAccum;

        
        public EntityBehaviorBreathe(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            base.Initialize(properties, typeAttributes);

            oxygenTree = entity.WatchedAttributes.GetTreeAttribute("oxygen");
            if (oxygenTree == null)
            {
                entity.WatchedAttributes.SetAttribute("oxygen", oxygenTree = new TreeAttribute());

                float maxoxy = 40000;
                if (entity is EntityPlayer) maxoxy = entity.World.Config.GetAsInt("lungCapacity", 40000);

                MaxOxygen = typeAttributes["maxoxygen"].AsFloat(maxoxy);
                Oxygen = typeAttributes["currentoxygen"].AsFloat(MaxOxygen);
                HasAir = true;
            }
            else maxOxygen = oxygenTree.GetFloat("maxoxygen");

            breathAccum = (float)entity.World.Rand.NextDouble();
        }

        public override void OnEntityRevive()
        {
            Oxygen = MaxOxygen;
        }

        public void Check()
        {
            maxOxygen = oxygenTree.GetFloat("maxoxygen");   // Periodically update this, it does not normally change unless a /player maxoxy command is used, but a mod could have changed this
            if (entity.World.Side == EnumAppSide.Client) return;

            bool nowHasAir = true;

            if (entity is EntityPlayer)
            {
                EntityPlayer plr = (EntityPlayer)entity;
                EnumGameMode mode = entity.World.PlayerByUid(plr.PlayerUID).WorldData.CurrentGameMode;
                if (mode == EnumGameMode.Creative || mode == EnumGameMode.Spectator)
                {
                    HasAir = true;
                    return;
                }
            }

            var eyeHeight = entity.Swimming ? entity.Properties.SwimmingEyeHeight : entity.Properties.EyeHeight;
            var eyeHeightMod1 = (entity.SidedPos.Y + eyeHeight) % 1;

            BlockPos pos = new BlockPos(
                (int)(entity.SidedPos.X + entity.LocalEyePos.X),
                (int)(entity.SidedPos.Y + eyeHeight),
                (int)(entity.SidedPos.Z + entity.LocalEyePos.Z),
                entity.SidedPos.Dimension
            );

            Block block = entity.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.FluidOrSolid);
            if (block.Attributes?["asphyxiating"].AsBool(true) != false)
            {
                Cuboidf[] collisionboxes = block.GetCollisionBoxes(entity.World.BlockAccessor, pos);

                Cuboidf box = new Cuboidf();
                if (collisionboxes != null)
                {
                    for (int i = 0; i < collisionboxes.Length; i++)
                    {
                        box.Set(collisionboxes[i]);
                        box.OmniGrowBy(-padding);
                        tmp.Set(pos.X + box.X1, pos.Y + box.Y1, pos.Z + box.Z1, pos.X + box.X2, pos.Y + box.Y2, pos.Z + box.Z2);
                        box.OmniGrowBy(padding);

                        if (tmp.Contains(entity.ServerPos.X + entity.LocalEyePos.X, entity.ServerPos.Y + entity.LocalEyePos.Y, entity.ServerPos.Z + entity.LocalEyePos.Z))
                        {
                            Cuboidd EntitySuffocationBox = entity.SelectionBox.ToDouble();

                            if (tmp.Intersects(EntitySuffocationBox))
                            {
                                nowHasAir = false;
                                suffocationSourceBlock = block;
                                break;
                            }
                        }
                    }
                }
            }

            if (block.IsLiquid() && block.LiquidLevel / 7f > eyeHeightMod1)
            {
                nowHasAir = false;
            }

            HasAir = nowHasAir;
        }


        public override void OnGameTick(float deltaTime)
        {
            if (entity.State == EnumEntityState.Inactive)
            {
                return;
            }

            if (!HasAir)
            {
                float oxygen = Math.Max(0, Oxygen - deltaTime * 1000);
                Oxygen = oxygen;

                if (oxygen <= 0)
                {
                    damageAccum += deltaTime;
                    if (damageAccum > 0.75)
                    {
                        damageAccum = 0;
                        DamageSource dmgsrc = new DamageSource() { Source = EnumDamageSource.Block, SourceBlock = suffocationSourceBlock, Type = EnumDamageType.Suffocation };
                        entity.ReceiveDamage(dmgsrc, 0.5f);
                    }
                }
            } else
            {
                Oxygen = Math.Min(MaxOxygen, Oxygen + deltaTime * 10000);
            }

            base.OnGameTick(deltaTime);

            breathAccum += deltaTime;

            if (breathAccum > 1)
            {
                breathAccum = 0;
                Check();
            }
        }

        public override string PropertyName()
        {
            return "breathe";
        }
    }
}
