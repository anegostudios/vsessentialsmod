using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Entity that represents a falling block like sand. When spawned it sets an air block at the initial 
    /// position. When it hits the ground it despawns and sets the original block back at that location.
    /// </summary>
    public class EntityBlockFalling : Entity
    {
        private int ticksAlive;
        int lingerTicks;

        public bool InitialBlockRemoved;
        

        private AssetLocation blockCode;
        public BlockPos initialPos;
        private ItemStack[] drops;
        public TreeAttribute blockEntityAttributes;
        public string blockEntityClass;

        public BlockEntity removedBlockentity;

        float impactDamageMul;

        bool fallHandled;
        float dustIntensity;

        byte[] lightHsv;
        AssetLocation fallSound;
        ILoadedSound sound;
        float soundStartDelay;

        ItemStack blockAsStack;
        bool canFallSideways;

        static SimpleParticleProperties dustParticles;
        static SimpleParticleProperties bitsParticles;

        Vec3d fallMotion = new Vec3d();
        float pushaccum;

        static HashSet<long> fallingNow = new HashSet<long>();


        // Additional config options
        public bool DoRemoveBlock = true;
        

        static EntityBlockFalling()
        {
            dustParticles = new SimpleParticleProperties(1, 3, ColorUtil.ToRgba(40, 220, 220, 220), new Vec3d(), new Vec3d(), new Vec3f(-0.25f, -0.25f, -0.25f), new Vec3f(0.25f, 0.25f, 0.25f), 1, 1, 0.3f, 0.3f, EnumParticleModel.Quad);
            dustParticles.AddQuantity = 5;
            dustParticles.MinVelocity.Set(-0.05f, -0.4f, -0.05f);
            dustParticles.AddVelocity.Set(0.1f, 0.2f, 0.1f);
            dustParticles.WithTerrainCollision = true;
            dustParticles.ParticleModel = EnumParticleModel.Quad;
            dustParticles.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.QUADRATIC, -16f);
            dustParticles.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.QUADRATIC, 3f);
            dustParticles.GravityEffect = 0;
            dustParticles.MaxSize = 1.3f;
            dustParticles.LifeLength = 3f;
            dustParticles.SelfPropelled = true;
            dustParticles.AddPos.Set(1.4, 1.4, 1.4);


            bitsParticles = new SimpleParticleProperties(1, 3, ColorUtil.ToRgba(40, 220, 220, 220), new Vec3d(), new Vec3d(), new Vec3f(-0.25f, -0.25f, -0.25f), new Vec3f(0.25f, 0.25f, 0.25f), 1, 1, 0.1f, 0.3f, EnumParticleModel.Quad);
            bitsParticles.AddPos.Set(1.4, 1.4, 1.4);
            bitsParticles.AddQuantity = 20;
            bitsParticles.MinVelocity.Set(-0.25f, 0, -0.25f);
            bitsParticles.AddVelocity.Set(0.5f, 1, 0.5f);
            bitsParticles.WithTerrainCollision = true;
            bitsParticles.ParticleModel = EnumParticleModel.Cube;
            bitsParticles.LifeLength = 1.5f;
            bitsParticles.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.QUADRATIC, -0.5f);
            bitsParticles.GravityEffect = 2.5f;
            bitsParticles.MinSize = 0.5f;
            bitsParticles.MaxSize = 1.5f;
        }

        public EntityBlockFalling() { }

        public override float MaterialDensity => 99999;
        
        public override byte[] LightHsv
        {
            get
            {
                return lightHsv;
            }
        }


        public EntityBlockFalling (Block block, BlockEntity blockEntity, BlockPos initialPos, AssetLocation fallSound, float impactDamageMul, bool canFallSideways, float dustIntensity)
        {
            this.impactDamageMul = impactDamageMul;
            this.fallSound = fallSound;
            this.canFallSideways = canFallSideways;
            this.dustIntensity = dustIntensity;

            WatchedAttributes.SetBool("canFallSideways", canFallSideways);
            WatchedAttributes.SetFloat("dustIntensity", dustIntensity);
            if (fallSound != null)
            {
                WatchedAttributes.SetString("fallSound", fallSound.ToShortString());
            }

            this.Code = new AssetLocation("blockfalling");
            this.blockCode = block.Code;
            this.removedBlockentity = blockEntity;
            this.initialPos = initialPos;

            ServerPos.SetPos(initialPos);
            ServerPos.X += 0.5;
            ServerPos.Z += 0.5;

            Pos.SetFrom(ServerPos);
        }


        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            if (removedBlockentity != null)
            {
                this.blockEntityAttributes = new TreeAttribute();
                removedBlockentity.ToTreeAttributes(blockEntityAttributes);
                blockEntityClass = api.World.ClassRegistry.GetBlockEntityClass(removedBlockentity.GetType());
            }

            SimulationRange = (int)(0.75f * GlobalConstants.DefaultTrackingRange);
            base.Initialize(properties, api, InChunkIndex3d);

            // Need to capture this now before we remove the block and start to fall
            drops = Block.GetDrops(api.World, initialPos, null);

            lightHsv = Block.GetLightHsv(World.BlockAccessor, initialPos);

			SidedPos.Motion.Y = -0.02;
            blockAsStack = new ItemStack(Block);

            
            if (api.Side == EnumAppSide.Client && fallSound != null && fallingNow.Count < 100)
            {
                fallingNow.Add(EntityId);
                ICoreClientAPI capi = api as ICoreClientAPI;
                sound = capi.World.LoadSound(new SoundParams()
                {
                    Location = fallSound.WithPathPrefixOnce("sounds/").WithPathAppendixOnce(".ogg"),
                    Position = new Vec3f((float)Pos.X, (float)Pos.Y, (float)Pos.Z),
                    Range = 32,
                    Pitch = 0.8f + (float)capi.World.Rand.NextDouble() * 0.3f,
                    Volume = 1,
                    SoundType = EnumSoundType.Ambient

                });
                soundStartDelay = 0.05f + (float)capi.World.Rand.NextDouble() / 3f;
            }

            canFallSideways = WatchedAttributes.GetBool("canFallSideways");
            dustIntensity = WatchedAttributes.GetFloat("dustIntensity");


            if (WatchedAttributes.HasAttribute("fallSound"))
            {
                fallSound = new AssetLocation(WatchedAttributes.GetString("fallSound"));
            }
        }

        
        /// <summary>
        /// Delays behaviors from ticking to reduce flickering
        /// </summary>
        /// <param name="dt"></param>
        public override void OnGameTick(float dt)
        {
            if (soundStartDelay > 0)
            {
                soundStartDelay -= dt;
                if (soundStartDelay <= 0)
                {
                    sound.Start();
                }
            }
            if (sound != null)
            {
                sound.SetPosition((float)Pos.X, (float)Pos.Y, (float)Pos.Z);
            }


            if (lingerTicks > 0)
            {
                lingerTicks--;
                if (lingerTicks == 0)
                {
                    if (Api.Side == EnumAppSide.Client && sound != null)
                    {
                        sound.FadeOut(3f, (s) => { s.Dispose(); });
                    }
                    Die();
                }

                return;
            }

            if (!Collided && !fallHandled)
            {
                spawnParticles(0);
            }


            ticksAlive++;
            if (ticksAlive >= 2 || Api.World.Side == EnumAppSide.Client) // Seems like we have to do it instantly on the client, otherwise we miss the OnChunkRetesselated Event
            {
                if (!InitialBlockRemoved)
                {
                    InitialBlockRemoved = true;
                    UpdateBlock(true, initialPos);
                }

                foreach (EntityBehavior behavior in SidedProperties.Behaviors)
                {
                    behavior.OnGameTick(dt);
                }
            }

            pushaccum += dt;
            fallMotion.X *= 0.99f;
            fallMotion.Z *= 0.99f;
            if (pushaccum > 0.2f)
            {
                pushaccum = 0;
                if (!Collided)
                {
                    Entity[] entities = World.GetEntitiesAround(SidedPos.XYZ, 1.1f, 1.1f, (e) => !(e is EntityBlockFalling));
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (Api.Side == EnumAppSide.Server || entities[i] is EntityPlayer)
                        {
                            entities[i].SidedPos.Motion.Add(fallMotion.X / 10f, 0, fallMotion.Z / 10f);
                        }
                    }
                }
            }


            if (Api.Side == EnumAppSide.Server && !Collided && World.Rand.NextDouble() < 0.01)
            {
                World.BlockAccessor.TriggerNeighbourBlockUpdate(ServerPos.AsBlockPos);
            }

            if (CollidedVertically && Pos.Motion.Length() == 0)
            {
                OnFallToGround(0);
            }
        }

        public override void OnEntityDespawn(EntityDespawnReason despawn)
        {
            base.OnEntityDespawn(despawn);
            
            if (Api.World.Side == EnumAppSide.Client)
            {
                fallingNow.Remove(EntityId);
            }
        }

        private void UpdateBlock(bool remove, BlockPos pos)
        {
            if (remove)
            {
                if (DoRemoveBlock)
                {
                    World.BlockAccessor.SetBlock(0, pos);
                    World.BlockAccessor.MarkBlockDirty(pos, () => OnChunkRetesselated(true));
                }
            } else
            {
                World.BlockAccessor.SetBlock(Block.BlockId, pos);
                World.BlockAccessor.MarkBlockDirty(pos, () => OnChunkRetesselated(false));

                if (blockEntityAttributes != null)
                {
                    BlockEntity be = World.BlockAccessor.GetBlockEntity(pos);

                    blockEntityAttributes.SetInt("posx", pos.X);
                    blockEntityAttributes.SetInt("posy", pos.Y);
                    blockEntityAttributes.SetInt("posz", pos.Z);

                    if (be != null)
                    {
                        be.FromTreeAttributes(blockEntityAttributes, World);
                    }
                }
            }
            
            NotifyNeighborsOfBlockChange(pos);
        }


        void spawnParticles(float smokeAdd)
        {
            if (Api.Side == EnumAppSide.Server || dustIntensity == 0) return;

            dustParticles.Color = Block.GetRandomColor(Api as ICoreClientAPI, blockAsStack);
            dustParticles.Color &= 0xffffff;
            dustParticles.Color |= (150 << 24);
            dustParticles.MinPos.Set(Pos.X - 0.2 - 0.5, Pos.Y, Pos.Z - 0.2 - 0.5);
            dustParticles.MinSize = 1f;

            float veloMul = smokeAdd / 20f;
            dustParticles.MinVelocity.Set(-0.2f * veloMul, 1f * (float)Pos.Motion.Y, -0.2f * veloMul);
            dustParticles.AddVelocity.Set(0.4f * veloMul, 0.2f * (float)Pos.Motion.Y + -veloMul, 0.4f * veloMul);
            dustParticles.MinQuantity = smokeAdd * dustIntensity;
            dustParticles.AddQuantity = (6 * Math.Abs((float)Pos.Motion.Y) + smokeAdd) * dustIntensity;

            
            
            Api.World.SpawnParticles(dustParticles);

            bitsParticles.MinPos.Set(Pos.X - 0.2 - 0.5, Pos.Y - 0.5, Pos.Z - 0.2 - 0.5);
            
            bitsParticles.MinVelocity.Set(-2f, 30f * (float)Pos.Motion.Y, -2f);
            bitsParticles.AddVelocity.Set(4f, 0.2f * (float)Pos.Motion.Y, 4f);
            bitsParticles.AddQuantity = 12 * Math.Abs((float)Pos.Motion.Y);
            bitsParticles.Color = dustParticles.Color;

            Api.World.SpawnParticles(bitsParticles);
        }


        private void OnChunkRetesselated(bool on)
        {
            EntityBlockFallingRenderer renderer = (Properties.Client.Renderer as EntityBlockFallingRenderer);
            if (renderer != null) renderer.DoRender = on;
        }

        private void NotifyNeighborsOfBlockChange(BlockPos pos)
        {
            foreach (BlockFacing facing in BlockFacing.ALLFACES)
            {
                BlockPos npos = pos.AddCopy(facing);
                Block neib = World.BlockAccessor.GetBlock(npos);
                neib.OnNeighbourBlockChange(World, npos, pos);
            }
        }

        public override void OnFallToGround(double motionY)
        {
            if (fallHandled) return;

            BlockPos pos = SidedPos.AsBlockPos;
            BlockPos finalPos = ServerPos.AsBlockPos;
            Block block = null;

            if (Api.Side == EnumAppSide.Server)
            {
                block = World.BlockAccessor.GetBlock(finalPos);

                if (block.OnFallOnto(World, finalPos, Block, blockEntityAttributes))
                {
                    lingerTicks = 3;
                    fallHandled = true;
                    return;
                }
            }

            if (canFallSideways)
            {
                for (int i = 0; i < 4; i++)
                {
                    BlockFacing facing = BlockFacing.HORIZONTALS[i];
                    if (
                        World.BlockAccessor.GetBlock(pos.X + facing.Normali.X, pos.Y + facing.Normali.Y, pos.Z + facing.Normali.Z).Replaceable >= 6000 &&
                        World.BlockAccessor.GetBlock(pos.X + facing.Normali.X, pos.Y + facing.Normali.Y - 1, pos.Z + facing.Normali.Z).Replaceable >= 6000)
                    {
                        if (Api.Side == EnumAppSide.Server)
                        {
                            SidedPos.X += facing.Normali.X;
                            SidedPos.Y += facing.Normali.Y;
                            SidedPos.Z += facing.Normali.Z;
                        }

                        fallMotion.Set(facing.Normalf.X, 0, facing.Normalf.Z);
                        spawnParticles(0);
                        return;
                    }
                }
            }

            spawnParticles(20f);
            
            
            Block blockAtFinalPos = World.BlockAccessor.GetBlock(finalPos);

            if (Api.Side == EnumAppSide.Server)
            {
                if (!block.IsReplacableBy(Block))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        BlockFacing facing = BlockFacing.HORIZONTALS[i];
                        block = World.BlockAccessor.GetBlock(finalPos.X + facing.Normali.X, finalPos.Y + facing.Normali.Y, finalPos.Z + facing.Normali.Z);

                        if (block.Replaceable >= 6000)
                        {
                            finalPos.X += facing.Normali.X;
                            finalPos.Y += facing.Normali.Y;
                            finalPos.Z += facing.Normali.Z;
                            break;
                        }
                    }
                }

                if (block.IsReplacableBy(Block))
                {
                    if (!block.IsLiquid() || Block.BlockMaterial != EnumBlockMaterial.Snow)
                    {
                        UpdateBlock(false, finalPos);
                    }

                    (Api as ICoreServerAPI).Network.BroadcastEntityPacket(EntityId, 1234);
                }
                else
                {
                    // Space is occupied by maybe a torch or some other block we shouldn't replace
                    DropItems(finalPos);
                }

                
                if (impactDamageMul > 0)
                {
                    Entity[] entities = World.GetEntitiesInsideCuboid(finalPos, finalPos.AddCopy(1, 1, 1));
                    foreach (var entity in entities)
                    {
                        entity.ReceiveDamage(new DamageSource() { Source = EnumDamageSource.Block, Type = EnumDamageType.Crushing, SourceBlock = Block, SourcePos = finalPos.ToVec3d() }, 6 * (float)Math.Abs(motionY) * impactDamageMul);
                    }
                }
            }

            lingerTicks = 50;
            fallHandled = true;
        }


        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            base.OnReceivedServerPacket(packetid, data);

            if (packetid == 1234)
            {
                EntityBlockFallingRenderer renderer = (Properties.Client.Renderer as EntityBlockFallingRenderer);
                if (renderer != null)
                {
                    World.BlockAccessor.MarkBlockDirty(Pos.AsBlockPos, () => OnChunkRetesselated(false));
                }

                lingerTicks = 50;
                fallHandled = true;
                spawnParticles(20f);
            }
        }

        private void DropItems(BlockPos pos)
        {
            if (drops != null)
            {
                for (int i = 0; i < drops.Length; i++)
                {
                    World.SpawnItemEntity(drops[i], pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }
        }

        /// <summary>
        /// The Block that is falling
        /// </summary>
        public Block Block
        {
            get { return World.BlockAccessor.GetBlock(blockCode); }
        }

        public override void ToBytes(BinaryWriter writer, bool forClient)
        {
            base.ToBytes(writer, forClient);

            writer.Write(initialPos.X);
            writer.Write(initialPos.Y);
            writer.Write(initialPos.Z);
            writer.Write(blockCode.ToShortString());
            writer.Write(blockEntityAttributes == null);

            if (blockEntityAttributes != null)
            {
                blockEntityAttributes.ToBytes(writer);
                writer.Write(blockEntityClass);
            }

            writer.Write(DoRemoveBlock);
        }

        public override void FromBytes(BinaryReader reader, bool forClient)
        {
            base.FromBytes(reader, forClient);

            initialPos = new BlockPos();
            initialPos.X = reader.ReadInt32();
            initialPos.Y = reader.ReadInt32();
            initialPos.Z = reader.ReadInt32();
            blockCode = new AssetLocation(reader.ReadString());

            bool beIsNull = reader.ReadBoolean();
            if (!beIsNull)
            {
                blockEntityAttributes = new TreeAttribute();
                blockEntityAttributes.FromBytes(reader);
                blockEntityClass = reader.ReadString();
            }

            if (WatchedAttributes.HasAttribute("fallSound"))
            {
                fallSound = new AssetLocation(WatchedAttributes.GetString("fallSound"));
            }

            canFallSideways = WatchedAttributes.GetBool("canFallSideways");
            dustIntensity = WatchedAttributes.GetFloat("dustIntensity");

            DoRemoveBlock = reader.ReadBoolean();
        }

        public override bool ShouldReceiveDamage(DamageSource damageSource, float damage)
        {
            return false;
        }

        public override bool IsInteractable
        {
            get { return false; }
        }
        
    }
}