using System;
using System.Collections.Concurrent;
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
    public class FallingBlockParticlesModSystem : ModSystem
    {
        static SimpleParticleProperties dustParticles;
        static SimpleParticleProperties bitsParticles;

        static FallingBlockParticlesModSystem()
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

        ICoreClientAPI capi;
        HashSet<EntityBlockFalling> fallingBlocks = new HashSet<EntityBlockFalling>();
        ConcurrentQueue<EntityBlockFalling> toRegister = new ConcurrentQueue<EntityBlockFalling>();
        ConcurrentQueue<EntityBlockFalling> toRemove = new ConcurrentQueue<EntityBlockFalling>();

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Client;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            this.capi = api;
            api.Event.RegisterAsyncParticleSpawner(asyncParticleSpawn);
        }

        public void Register(EntityBlockFalling entity)
        {
            toRegister.Enqueue(entity);
        }

        public void Unregister(EntityBlockFalling entity)
        {
            toRemove.Enqueue(entity);
        }

        public int ActiveFallingBlocks => fallingBlocks.Count;


        private bool asyncParticleSpawn(float dt, IAsyncParticleManager manager)
        {
            int alive = manager.ParticlesAlive(EnumParticleModel.Quad);

            // Reduce particle spawn the more falling blocks there are
            // http://fooplot.com/#W3sidHlwZSI6MCwiZXEiOiIwLjk1Xih4LzUpIiwiY29sb3IiOiIjMDAwMDAwIn0seyJ0eXBlIjoxMDAwLCJ3aW5kb3ciOlsiMCIsIjIwMCIsIjAiLCIxIl19XQ--
            float particlemul = Math.Max(0.05f, (float)Math.Pow(0.95f, alive / 200f));

            foreach (var bef in fallingBlocks)
            {
                //if (api.Side == EnumAppSide.Server || dustIntensity == 0) return;

                float dustAdd = 0f;
                if (bef.nowImpacted)
                {
                    var lblock = capi.World.BlockAccessor.GetBlock(bef.Pos.AsBlockPos, BlockLayersAccess.Fluid);
                    // No dust under water
                    if (lblock.Id == 0)
                    {
                        dustAdd = 20f;
                    }
                    bef.nowImpacted = false;
                }

                if (bef.Block.Id != 0)
                {
                    dustParticles.Color = bef.stackForParticleColor.Collectible.GetRandomColor(capi, bef.stackForParticleColor);
                    dustParticles.Color &= 0xffffff;
                    dustParticles.Color |= (150 << 24);
                    dustParticles.MinPos.Set(bef.Pos.X - 0.2 - 0.5, bef.Pos.Y, bef.Pos.Z - 0.2 - 0.5);
                    dustParticles.MinSize = 1f;

                    float veloMul = dustAdd / 20f;
                    dustParticles.MinVelocity.Set(-0.2f * veloMul, 1f * (float)bef.Pos.Motion.Y, -0.2f * veloMul);
                    dustParticles.AddVelocity.Set(0.4f * veloMul, 0.2f * (float)bef.Pos.Motion.Y + -veloMul, 0.4f * veloMul);
                    dustParticles.MinQuantity = dustAdd * bef.dustIntensity * particlemul / 2f;
                    dustParticles.AddQuantity = (6 * Math.Abs((float)bef.Pos.Motion.Y) + dustAdd) * bef.dustIntensity * particlemul / 2f;

                    manager.Spawn(dustParticles);
                }

                bitsParticles.MinPos.Set(bef.Pos.X - 0.2 - 0.5, bef.Pos.Y - 0.5, bef.Pos.Z - 0.2 - 0.5);

                bitsParticles.MinVelocity.Set(-2f, 30f * (float)bef.Pos.Motion.Y, -2f);
                bitsParticles.AddVelocity.Set(4f, 0.2f * (float)bef.Pos.Motion.Y, 4f);
                bitsParticles.MinQuantity = particlemul;
                bitsParticles.AddQuantity = 6 * Math.Abs((float)bef.Pos.Motion.Y) * particlemul;
                bitsParticles.Color = dustParticles.Color;

                dustParticles.Color = bef.Block.GetRandomColor(capi, bef.stackForParticleColor);

                capi.World.SpawnParticles(bitsParticles);
            }

            int cnt = toRegister.Count;
            while (cnt-- > 0)
            {
                if (toRegister.TryDequeue(out EntityBlockFalling bef))
                {
                    fallingBlocks.Add(bef);
                }
            }

            cnt = toRemove.Count;
            while (cnt-- > 0)
            {
                if (toRemove.TryDequeue(out EntityBlockFalling bef))
                {
                    fallingBlocks.Remove(bef);
                }
            }

            return true;
        }
    }



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
        internal float dustIntensity;
        internal float nowDustIntensity;

        byte[] lightHsv;
        AssetLocation fallSound;
        ILoadedSound sound;
        float soundStartDelay;

        internal ItemStack stackForParticleColor;
        bool canFallSideways;


        Vec3d fallMotion = new Vec3d();
        float pushaccum;

        static HashSet<long> fallingNow = new HashSet<long>();


        // Additional config options
        public bool DoRemoveBlock = true;

        internal bool nowImpacted;

        public EntityBlockFalling() { }

        public override float MaterialDensity => 99999;

        public override byte[] LightHsv
        {
            get
            {
                return lightHsv;
            }
        }

        EntityBehaviorPassivePhysics physicsBh;
        FallingBlockParticlesModSystem particleSys;

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
            this.initialPos = initialPos.Copy(); // Must have a Copy() here!

            ServerPos.SetPos(initialPos);
            ServerPos.X += 0.5;
            ServerPos.Y += 0.01;
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

            SimulationRange = (int)(0.75f * GlobalConstants.DefaultSimulationRange);
            base.Initialize(properties, api, InChunkIndex3d);

            // Need to capture this now before we remove the block and start to fall
            drops = Block.GetDrops(api.World, initialPos, null);

            lightHsv = Block.GetLightHsv(World.BlockAccessor, initialPos);

			//SidedPos.Motion.Y = -0.02;

            if (drops != null && drops.Length > 0)
            {
                stackForParticleColor = drops[0];
            }
            else
            {
                stackForParticleColor = new ItemStack(Block);
            }

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
                sound.Start();
                soundStartDelay = 0.05f + (float)capi.World.Rand.NextDouble() / 3f;
            }

            canFallSideways = WatchedAttributes.GetBool("canFallSideways");
            dustIntensity = WatchedAttributes.GetFloat("dustIntensity");


            if (WatchedAttributes.HasAttribute("fallSound"))
            {
                fallSound = new AssetLocation(WatchedAttributes.GetString("fallSound"));
            }

            if (api.World.Side == EnumAppSide.Client)
            {
                particleSys = api.ModLoader.GetModSystem<FallingBlockParticlesModSystem>();
                particleSys.Register(this);
                physicsBh = GetBehavior<EntityBehaviorPassivePhysics>();
            }
        }


        /// <summary>
        /// Delays behaviors from ticking to reduce flickering
        /// </summary>
        /// <param name="dt"></param>
        public override void OnGameTick(float dt)
        {
            World.FrameProfiler.Enter("entity-tick-unsstablefalling");

            // (1 - 0.95^(x/100)) / 2
            // http://fooplot.com/#W3sidHlwZSI6MCwiZXEiOiIoMS0wLjk1Xih4LzEwMCkpLzIiLCJjb2xvciI6IiMwMDAwMDAifSx7InR5cGUiOjEwMDAsIndpbmRvdyI6WyIwIiwiMzAwMCIsIjAiLCIxIl19XQ--
            // if (physicsBh != null)
            // {
            //     physicsBh.clientPhysicsTickTimeThreshold = (float)((1 - Math.Pow(0.95, particleSys.ActiveFallingBlocks / 100.0)) / 4.0);
            // }

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
                nowDustIntensity = 1;
            } else
            {
                nowDustIntensity = 0;
            }

            World.FrameProfiler.Mark("entity-tick-unsstablefalling-sound(etc)");

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
                World.FrameProfiler.Mark("entity-tick-unsstablefalling-physics(etc)");
            }

            pushaccum += dt;
            fallMotion.X *= 0.99f;
            fallMotion.Z *= 0.99f;
            if (pushaccum > 0.2f)
            {
                pushaccum = 0;
                if (!Collided)
                {
                    Entity[] entities;
                    if (Api.Side == EnumAppSide.Server)
                    {
                        entities = World.GetEntitiesAround(SidedPos.XYZ, 1.1f, 1.1f, (e) => !(e is EntityBlockFalling));

                        bool didhit = false;
                        foreach (var entity in entities)
                        {
                            bool nowhit = entity.ReceiveDamage(new DamageSource() { Source = EnumDamageSource.Block, Type = EnumDamageType.Crushing, SourceBlock = Block, SourcePos = SidedPos.XYZ }, 10 * (float)Math.Abs(ServerPos.Motion.Y) * impactDamageMul);
                            if (nowhit && !didhit)
                            {
                                didhit = nowhit;
                                Api.World.PlaySoundAt(this.Block.Sounds.Break, entity);
                            }
                        }
                    }
                    else
                    {
                        entities = World.GetEntitiesAround(SidedPos.XYZ, 1.1f, 1.1f, (e) => e is EntityPlayer);
                    }

                    for (int i = 0; i < entities.Length; i++)
                    {
                        entities[i].SidedPos.Motion.Add(fallMotion.X / 10f, 0, fallMotion.Z / 10f);
                    }
                }
            }


            World.FrameProfiler.Mark("entity-tick-unsstablefalling-finalizemotion");
            if (Api.Side == EnumAppSide.Server && !Collided && World.Rand.NextDouble() < 0.01)
            {
                World.BlockAccessor.TriggerNeighbourBlockUpdate(ServerPos.AsBlockPos);
                World.FrameProfiler.Mark("entity-tick-unsstablefalling-neighborstrigger");
            }

            if (CollidedVertically && Pos.Motion.Length() == 0)
            {
                OnFallToGround(0);
                World.FrameProfiler.Mark("entity-tick-unsstablefalling-falltoground");
            }

            World.FrameProfiler.Leave();
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);

            if (Api.World.Side == EnumAppSide.Client)
            {
                fallingNow.Remove(EntityId);
                particleSys.Unregister(this);
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
                var lbock = World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
                if (lbock.Id == 0 || Block.BlockMaterial != EnumBlockMaterial.Snow)
                {
                    World.BlockAccessor.SetBlock(Block.BlockId, pos);
                    World.BlockAccessor.MarkBlockDirty(pos, () => OnChunkRetesselated(false));
                }

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

            World.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        }





        private void OnChunkRetesselated(bool on)
        {
            EntityBlockFallingRenderer renderer = (Properties.Client.Renderer as EntityBlockFallingRenderer);
            if (renderer != null) renderer.DoRender = on;
        }


        public override void OnFallToGround(double motionY)
        {
            if (fallHandled) return;

            BlockPos pos = SidedPos.AsBlockPos;
            BlockPos finalPos = ServerPos.AsBlockPos;
            Block block = null;

            if (Api.Side == EnumAppSide.Server)
            {
                block = World.BlockAccessor.GetMostSolidBlock(finalPos);

                if (block.OnFallOnto(World, finalPos, Block, blockEntityAttributes))
                {
                    lingerTicks = 3;
                    fallHandled = true;
                    return;
                }
            }

            if (canFallSideways)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (i == 4) continue;
                    BlockFacing facing = BlockFacing.ALLFACES[i];

                    var nblock = World.BlockAccessor.GetMostSolidBlock(pos.X + facing.Normali.X, pos.Y + facing.Normali.Y, pos.Z + facing.Normali.Z);
                    if (nblock.Replaceable >= 6000)
                    {
                        nblock = World.BlockAccessor.GetMostSolidBlock(pos.X + facing.Normali.X, pos.Y + facing.Normali.Y - 1, pos.Z + facing.Normali.Z);
                        if (nblock.Replaceable >= 6000)
                        {
                            if (Api.Side == EnumAppSide.Server)
                            {
                                SidedPos.X += facing.Normali.X;
                                SidedPos.Y += facing.Normali.Y;
                                SidedPos.Z += facing.Normali.Z;
                            }

                            fallMotion.Set(facing.Normalf.X, 0, facing.Normalf.Z);
                            return;
                        }
                    }
                }
            }

            nowImpacted = true;


            if (Api.Side == EnumAppSide.Server)
            {
                if (block.Id != 0 && Block.BlockMaterial == EnumBlockMaterial.Snow)
                {
                    UpdateSnowLayer(finalPos, block);
                    (Api as ICoreServerAPI).Network.BroadcastEntityPacket(EntityId, 1234);
                }
                else if (block.IsReplacableBy(Block))
                {
                    UpdateBlock(false, finalPos);
                    (Api as ICoreServerAPI).Network.BroadcastEntityPacket(EntityId, 1234);
                }
                else
                {
                    if (block.Replaceable >= 6000)
                    {
                        DropItems(finalPos);
                    }
                    else
                    {
                        SidedPos.Y += 1;
                        return;
                    }
                }


                if (impactDamageMul > 0)
                {
                    Entity[] entities = World.GetEntitiesInsideCuboid(finalPos, finalPos.AddCopy(1, 1, 1), (e) => !(e is EntityBlockFalling));
                    bool didhit = false;
                    foreach (var entity in entities)
                    {
                        bool nowhit = entity.ReceiveDamage(new DamageSource() { Source = EnumDamageSource.Block, Type = EnumDamageType.Crushing, SourceBlock = Block, SourcePos = finalPos.ToVec3d() }, 18 * (float)Math.Abs(motionY) * impactDamageMul);
                        if (nowhit && !didhit)
                        {
                            didhit = nowhit;
                            Api.World.PlaySoundAt(this.Block.Sounds.Break, entity);
                        }
                    }
                }
            }

            lingerTicks = 50;
            fallHandled = true;
        }

        /// <summary>
        /// Simply update the snow level at the current position, no need to touch its BlockEntity (if there is one)
        /// </summary>
        private void UpdateSnowLayer(BlockPos finalPos, Block block)
        {
            Block snowblock = block.GetSnowCoveredVariant(finalPos, block.snowLevel + 1);
            if (snowblock != null && snowblock != block)
            {
                World.BlockAccessor.ExchangeBlock(snowblock.Id, finalPos);
            }
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
                nowImpacted = true;
                particleSys.Unregister(this);
            }
        }



        private void DropItems(BlockPos pos)
        {
            var dpos = pos.ToVec3d().Add(0.5, 0.5, 0.5);

            if (drops != null)
            {
                for (int i = 0; i < drops.Length; i++)
                {
                    World.SpawnItemEntity(drops[i], dpos);
                }
            }

            if (removedBlockentity is IBlockEntityContainer bec)
            {
                bec.DropContents(dpos);
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
