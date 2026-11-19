using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class UnregisterClothSystemPacket
    {
        public required int[] ClothIds;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ClothSystemPacket
    {
        public required ClothSystem[] ClothSystems;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ClothPointPacket
    {
        public int ClothId;
        public int PointX;
        public int PointY;
        public required ClothPoint Point;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ClothLengthPacket
    {
        public int ClothId;
        public double LengthChange;
    }

    public class ClothManager : ModSystem, IRenderer
	{
        int nextClothId = 1;

        ICoreClientAPI? capi;
        ICoreServerAPI? sapi;
        ICoreAPI api = null!;

        Dictionary<int, ClothSystem> clothSystems = new Dictionary<int, ClothSystem>();

        internal ParticlePhysics partPhysics = null!;

        public double RenderOrder => 1;

        public int RenderRange => 12;


        MeshRef? ropeMeshRef;
        MeshData? updateMesh;
        IShaderProgram? prog;
        ILoadedSound? stretchSound;

        public float accum3s;
        public float accum100ms;
        private IServerNetworkChannel? clothSystemChannel;

        public override double ExecuteOrder()
        {
            return 0.4;
        }


        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            this.api = api;

            partPhysics = new ParticlePhysics(api.World.GetLockFreeBlockAccessor());
            partPhysics.PhysicsTickTime = 1 / 60f;
            partPhysics.MotionCap = 10f;

            api.Network
                .RegisterChannel("clothphysics")
                .RegisterMessageType<UnregisterClothSystemPacket>()
                .RegisterMessageType<ClothSystemPacket>()
                .RegisterMessageType<ClothPointPacket>()
                .RegisterMessageType<ClothLengthPacket>()
            ;
        }


        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;


            api.ChatCommands.GetOrCreate("debug")
                .BeginSubCommand("clothtest")
                    .WithDescription("Commands to test the cloth system")

                    .BeginSubCommand("clear")
                        .WithDescription("clears")
                        .HandleWith(onClothTestClear)
                    .EndSubCommand()

                .EndSubCommand();

            api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "clothsimu");

            api.Event.BlockTexturesLoaded += Event_BlockTexturesLoaded;

            api.Network.GetChannel("clothphysics")
                .SetMessageHandler<UnregisterClothSystemPacket>(onUnregPacketClient)
                .SetMessageHandler<ClothSystemPacket>(onRegPacketClient)
                .SetMessageHandler<ClothPointPacket>(onPointPacketClient)
                .SetMessageHandler<ClothLengthPacket>(onLengthPacketClient)
            ;

            api.Event.LeaveWorld += Event_LeaveWorld;

        }


        public ClothSystem? GetClothSystem(int clothid)
        {
            clothSystems.TryGetValue(clothid, out ClothSystem? sys);
            return sys;
        }

        /// <summary>
        /// Only checks the first and last attachment point
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public ClothSystem? GetClothSystemAttachedToBlock(BlockPos pos)
        {
            foreach (var sys in clothSystems.Values)
            {
                if (sys.FirstPoint.PinnedToBlockPos == pos || sys.LastPoint.PinnedToBlockPos == pos)
                {
                    return sys;
                }
            }

            return null;
        }

        /// <summary>
        /// Only checks the first and last attachment point
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public ClothSystem? GetClothSystemAttachedToEntity(long entityid)
        {
            foreach (var sys in clothSystems.Values)
            {
                if (sys.FirstPoint.pinnedToEntityId == entityid || sys.LastPoint.pinnedToEntityId == entityid)
                {
                    return sys;
                }
            }

            return null;
        }



        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            if (updateMesh == null) return;
            ICoreClientAPI capi = this.capi!;

            dt = System.Math.Min(dt, 0.5f);
            if (!capi.IsGamePaused)
            {
                tickPhysics(dt);
            }

            accum100ms += dt;
            if (accum100ms > 0.1)
            {
                if (clothSystems.Count > 0)
                {
                    ClothSystem? maxcs = null;
                    float maxIntensity = 0;

                    foreach (var val in clothSystems)
                    {
                        var cs = val.Value;
                        if (!cs.CanRip) continue;

                        if (cs.MaxExtension > cs.StretchWarn)
                        {
                            cs.secondsOverStretched += accum100ms;
                        } else
                        {
                            cs.secondsOverStretched = 0;
                            continue;
                        }

                        if (cs.secondsOverStretched <= 0.2) continue;

                        float intensity = 1.5f * (float)(cs.MaxExtension - cs.StretchWarn) / Math.Max(cs.StretchRip - cs.StretchWarn, 0.001f);

                        if (intensity > maxIntensity)
                        {
                            maxcs = cs;
                            maxIntensity = intensity;
                        }
                    }

                    if (maxIntensity > 0 && maxcs != null && stretchSound != null)
                    {
                        if (!stretchSound.IsPlaying) stretchSound.Start();
                        stretchSound.SetPosition((float)maxcs.CenterPosition.X, (float)maxcs.CenterPosition.Y, (float)maxcs.CenterPosition.Z);
                        stretchSound.SetVolume(GameMath.Clamp((float)maxIntensity, 0.5f, 1f));
                        stretchSound.SetPitch((float)GameMath.Clamp(maxIntensity + 0.7f, 0.7f, 1.2f));
                    }
                    else
                    {
                        stretchSound?.Stop();
                    }
                } else
                {
                    stretchSound?.Stop();
                }

                accum100ms = 0;
            }

            int count = 0;
            updateMesh.CustomFloats.Count = 0;

            foreach (var val in clothSystems)
            {
                if (!val.Value.Active) continue;

                count += val.Value.UpdateMesh(updateMesh, dt);
                updateMesh.CustomFloats.Count = count * (4 + 16);
            }

            if (count > 0)
            {
                if (prog == null || prog.Disposed) prog = capi.Shader.GetProgramByName("instanced");

                capi.Render.GlToggleBlend(false); // Seems to break SSAO without
                prog.Use();
                prog.BindTexture2D("tex", capi.ItemTextureAtlas.Positions[0].atlasTextureId, 0);
                prog.Uniform("rgbaFogIn", capi.Render.FogColor);
                prog.Uniform("rgbaAmbientIn", capi.Render.AmbientColor);
                prog.Uniform("fogMinIn", capi.Render.FogMin);
                prog.Uniform("fogDensityIn", capi.Render.FogDensity);
                prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
                prog.UniformMatrix("modelViewMatrix", capi.Render.CameraMatrixOriginf);

                updateMesh.CustomFloats.Count = count * (4 + 16);
                capi.Render.UpdateMesh(ropeMeshRef, updateMesh);
                capi.Render.RenderMeshInstanced(ropeMeshRef, count);

                prog.Stop();
            }


            foreach (var val in clothSystems)
            {
                if (!val.Value.Active) continue;

                val.Value.CustomRender(dt);
            }
        }

        private void tickPhysics(float dt)
        {
            foreach (var val in clothSystems)
            {
                if (!val.Value.Active) continue;
                val.Value.updateFixedStep(dt);
            }

            if (sapi != null)
            {
                List<int> toRemove = new List<int>();

                accum100ms += dt;
                if (accum100ms > 0.1f)
                {
                    List<ClothPointPacket> packets = new List<ClothPointPacket>();

                    foreach ((int key, ClothSystem cs) in clothSystems)
                    {
                        if (!cs.CanRip || !cs.Active) continue;

                        cs.CollectDirtyPoints(packets);

                        // Overextended -> Rip it apart
                        if (cs.MaxExtension > cs.StretchRip)
                        {
                            cs.secondsOverStretched += accum100ms;

                            // Base of 2.5 seconds over-stretched to rip, quickened by stretching further
                            if (cs.secondsOverStretched > 2.5f * cs.StretchRip / Math.Max(0.001f, cs.MaxExtension))
                            {
                                Vec3d soundPos = cs.CenterPosition;

                                if (cs.FirstPoint.PinnedToEntity != null) soundPos = cs.FirstPoint.PinnedToEntity.Pos.XYZ;
                                sapi.World.PlaySoundAt(new AssetLocation("sounds/effect/roperip"), soundPos.X, soundPos.Y, soundPos.Z);

                                var fp = cs.FirstPoint;
                                var lp = cs.LastPoint;
                                var dir = lp.Pos - fp.Pos;
                                var len = dir.Length();

                                for (float i = 0; i < len; i += 0.15f)
                                {
                                    Vec3d pos = new Vec3d(fp.Pos.X + dir.X * i / len, fp.Pos.Y + dir.Y * i / len, fp.Pos.Z + dir.Z * i / len);

                                    sapi.World.SpawnParticles(2, ColorUtil.ColorFromRgba(60, 97, 115, 255), pos, pos, new Vec3f(-4f, -1f, -4f), new Vec3f(4, 2, 4), 2, 1, 0.5f, EnumParticleModel.Cube);
                                }

                                cs.FirstPoint.PinnedToEntity?.GetInterface<IRopeRippedListener>()?.OnRopeRipped(cs);
                                cs.LastPoint.PinnedToEntity?.GetInterface<IRopeRippedListener>()?.OnRopeRipped(cs);

                                toRemove.Add(key);
                            }
                        } else
                        {
                            cs.secondsOverStretched = 0;
                        }
                    }
                    accum100ms = 0;

                    foreach (var p in packets)
                    {
                        clothSystemChannel?.BroadcastPacket(p);
                    }
                }

                accum3s += dt;
                if (accum3s > 3)
                {
                    accum3s = 0;

                    foreach ((int key, ClothSystem clothSystem) in clothSystems)
                    {
                        if (!clothSystem.Active) continue;

                        if (!clothSystem.PinnedAnywhere)
                        {
                            toRemove.AddIfNotPresent(key);
                        } else
                        {
                            clothSystem.slowTick3s();
                        }
                    }
                }

                foreach (int id in toRemove)
                {
                    bool spawnitem = true;

                    var cs = clothSystems[id];
                    spawnitem &= (cs.FirstPoint.PinnedToEntity as EntityItem)?.Itemstack?.Collectible.Code.Path != "rope" &&
                                 (cs.LastPoint.PinnedToEntity as EntityItem)?.Itemstack?.Collectible.Code.Path != "rope";

                    if (cs.FirstPoint.PinnedToEntity is EntityAgent eagentn)
                    {
                        eagentn.WalkInventory((slot) =>
                        {
                            if (slot.Empty) return true;
                            int clothId = slot.Itemstack.Attributes.GetInt("clothId");
                            if (clothId == id)
                            {
                                spawnitem = false;
                                slot.Itemstack.Attributes.RemoveAttribute("clothId");
                                slot.Itemstack.Attributes.RemoveAttribute("ropeHeldByEntityId");
                                slot.MarkDirty();
                                return false;
                            }

                            return true;
                        });
                    }

                    if (cs.LastPoint.PinnedToEntity is EntityAgent eagentn2)
                    {
                        eagentn2.WalkInventory((slot) =>
                        {
                            if (slot.Empty) return true;
                            int clothId = slot.Itemstack.Attributes.GetInt("clothId");
                            if (clothId == id)
                            {
                                spawnitem = false;
                                slot.Itemstack.Attributes.RemoveAttribute("clothId");
                                slot.Itemstack.Attributes.RemoveAttribute("ropeHeldByEntityId");
                                slot.MarkDirty();
                                return false;
                            }

                            return true;
                        });
                    }

                    if (spawnitem)
                    {
                        sapi.World.SpawnItemEntity(new ItemStack(sapi.World.GetItem(new AssetLocation("rope"))), clothSystems[id].CenterPosition);
                    }
                    else
                    {
                        // if the first rope end is on the ground as an item delete it since one will be in the players inventory already
                        if (cs.FirstPoint.PinnedToEntity is EntityItem && cs.LastPoint.PinnedToEntity is EntityPlayer)
                        {
                            cs.FirstPoint.PinnedToEntity.Die(EnumDespawnReason.Removed);
                        }
                    }
                    UnregisterCloth(id);
                }
            }
        }


        public override bool ShouldLoad(EnumAppSide side)
        {   
            return true;
        }

        private void Event_LeaveWorld()
        {
            ropeMeshRef?.Dispose();
        }

        private void onPointPacketClient(ClothPointPacket msg)
        {
            if (clothSystems.TryGetValue(msg.ClothId, out var sys)) {
                sys.updatePoint(msg);
            }
        }

        private void onLengthPacketClient(ClothLengthPacket msg)
        {
            if (clothSystems.TryGetValue(msg.ClothId, out var sys)) {
                sys.ChangeRopeLength(msg.LengthChange);
            }
        }

        private void onRegPacketClient(ClothSystemPacket msg)
        {
            foreach (ClothSystem system in msg.ClothSystems)
            {
                system.Init(capi ?? throw new Exception("Expected onRegPacketClient to be called on the client"), this);
                system.restoreReferences();
                clothSystems[system.ClothId] = system;
            }
        }

        private void onUnregPacketClient(UnregisterClothSystemPacket msg)
        {
            foreach (int clothid in msg.ClothIds)
            {
                UnregisterCloth(clothid);
            }
        }

        private void Event_BlockTexturesLoaded()
        {
            ICoreClientAPI capi = this.capi!;

            stretchSound ??= capi.World.LoadSound(new SoundParams()
            {
                Location = new AssetLocation("sounds/effect/ropestretch"),
                DisposeOnFinish = false,
                ShouldLoop = true,
                SoundType = EnumSoundType.Sound,
                Volume = 0.5f,
                ReferenceDistance = 5
            });

            // This shader is created by the essentials mod in Core.cs
            prog = capi.Shader.GetProgramByName("instanced");

            Item? itemRope = capi.World.GetItem(new AssetLocation("rope"));

            Shape? shape = Shape.TryGet(capi, "shapes/item/ropesection.json");
            if (itemRope == null || shape == null) return;
            capi.Tesselator.TesselateShape(itemRope, shape, out MeshData meshData);


            updateMesh = new MeshData(false);
            updateMesh.CustomFloats = new CustomMeshDataPartFloat((16 + 4) * 10100)
            {
                Instanced = true,
                InterleaveOffsets = new int[] { 0, 16, 32, 48, 64 },
                InterleaveSizes = new int[] { 4, 4, 4, 4, 4 },
                InterleaveStride = 16 + 4 * 16,
                StaticDraw = false
            };
            updateMesh.CustomFloats.SetAllocationSize((16 + 4) * 10100);

            meshData.CustomFloats = updateMesh.CustomFloats;

            ropeMeshRef = capi.Render.UploadMesh(meshData);
            updateMesh.CustomFloats.Count = 0;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            base.StartServerSide(api);

            api.Event.RegisterGameTickListener(tickPhysics, 30);

            api.Event.ChunkColumnLoaded += Event_ChunkColumnLoaded;
            api.Event.ChunkColumnUnloaded += Event_ChunkColumnUnloaded;
            api.Event.MapRegionLoaded += Event_MapRegionLoaded;
            api.Event.MapRegionUnloaded += Event_MapRegionUnloaded;

            api.Event.SaveGameLoaded += Event_SaveGameLoaded;
            api.Event.GameWorldSave += Event_GameWorldSave;
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, updateActiveState);

            api.Event.PlayerJoin += Event_PlayerJoin;
            clothSystemChannel = api.Network.GetChannel("clothphysics");

            api.ChatCommands.GetOrCreate("debug")
                .BeginSubCommand("clothtest")
                    .WithDescription("Commands to test the cloth system")

                     .BeginSubCommand("cloth")
                        .WithDescription("cloth")
                        .HandleWith(onClothTestCloth)
                    .EndSubCommand()

                    .BeginSubCommand("rope")
                        .WithDescription("rope")
                        .HandleWith(onClothTestRope)
                    .EndSubCommand()

                    .BeginSubCommand("clear")
                        .WithDescription("clears")
                        .HandleWith(onClothTestClearServer)
                    .EndSubCommand()

                    .BeginSubCommand("deleteloaded")
                        .WithDescription("deleteloaded")
                        .HandleWith(onClothTestDeleteloaded)
                    .EndSubCommand()
                .EndSubCommand();
        }

        private void Event_ChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
        {
            // Minor bug: When this is called, the chunk hasn't been added to the list of loaded chunks yet, so this is actually loading the previous chunk column's ropes
            if (sapi!.Server.CurrentRunPhase >= EnumServerRunPhase.RunGame)
            {
                updateActiveState();
            }
        }

        private void Event_ChunkColumnUnloaded(Vec3i chunkCoord)
        {
            if (sapi!.Server.CurrentRunPhase >= EnumServerRunPhase.Shutdown) return;

            List<ClothSystem> deactivated = new();
            foreach ((_, ClothSystem clothSystem) in clothSystems)
            {
                if (!clothSystem.Active) continue;

                clothSystem.WalkPoints((p) => {
                    clothSystem.Active &= ((int)p.Pos.X) / GlobalConstants.ChunkSize != chunkCoord.X
                        // Skip Y - this is a column.
                        || ((int)p.Pos.Z) / GlobalConstants.ChunkSize != chunkCoord.Z;
                });

                if (!clothSystem.Active) deactivated.Add(clothSystem);
            }
            if (deactivated.Count > 0)
            {
                clothSystemChannel?.BroadcastPacket(new ClothSystemPacket() { ClothSystems = deactivated.ToArray() });
            }
        }

        private void updateActiveState()
        {
            List<ClothSystem> cs = new List<ClothSystem>();
            foreach (var sys in clothSystems.Values)
            {
                bool nowActive = sys.updateActiveState(EnumActiveStateChange.RegionNowLoaded);

                if (nowActive) {
                    cs.Add(sys);
                }
            }

            if (cs.Count > 0)
            {
                clothSystemChannel?.BroadcastPacket(new ClothSystemPacket() { ClothSystems = cs.ToArray() });
            }
        }

        private void Event_PlayerJoin(IServerPlayer byPlayer)
        {
            if (clothSystems.Values.Count > 0)
            {
                clothSystemChannel?.SendPacket(new ClothSystemPacket() { ClothSystems = clothSystems.Values.ToArray() }, byPlayer);
            }
        }

        private void Event_GameWorldSave()
        {
            ICoreServerAPI sapi = this.sapi!;
            sapi.WorldManager.SaveGame.StoreData("nextClothId", SerializerUtil.Serialize(nextClothId));

            foreach (var val in sapi.WorldManager.AllLoadedMapRegions)
            {
                Vec3i pos = sapi.WorldManager.MapRegionPosFromIndex2D(val.Key);
                saveRegion(new Vec2i(pos.X, pos.Z), val.Value);
            }
        }

        private void Event_SaveGameLoaded()
        {
            byte[]? data = sapi!.WorldManager.SaveGame.GetData("nextClothId");
            if (data != null)
            {
                nextClothId = SerializerUtil.Deserialize<int>(data);
            }
        }

        // Ropes stored by region, decided by the location of their starting point.
        // Simulation is stopped once any part of the rope is in an unloaded chunk,
        // preventing the starting point from moving to a different region after being saved.
        private List<ClothSystem> saveRegion(Vec2i mapCoord, IMapRegion region)
        {
            List<ClothSystem> systems = new List<ClothSystem>();

            int regionSize = sapi!.WorldManager.RegionSize;

            foreach (var cs in clothSystems.Values)
            {
                BlockPos pos = cs.FirstPoint.Pos.AsBlockPos;
                int regx = pos.X / regionSize;
                int regZ = pos.Z / regionSize;

                if (regx == mapCoord.X && regZ == mapCoord.Y)
                {
                    systems.Add(cs);
                }
            }

            if (systems.Count > 0)
            {
                region.SetModdata("clothSystems", SerializerUtil.Serialize(systems));
            }
            else if (region.GetModdata("clothSystems") != null)
            {
                region.RemoveModdata("clothSystems");
            }

            return systems;
        }

        private void Event_MapRegionUnloaded(Vec2i mapCoord, IMapRegion region)
        {
            // If shutting down, ropes will be saved from Event_GameWorldSave, so skip it here
            if (sapi!.Server.CurrentRunPhase >= EnumServerRunPhase.Shutdown) return;

            List<ClothSystem> systems = saveRegion(mapCoord, region);

            if (systems == null || systems.Count == 0) return;

            int[] clothIds = new int[systems.Count];
            for (int i = 0; i < systems.Count; i++)
            {
                clothSystems.Remove(systems[i].ClothId);
                clothIds[i] = systems[i].ClothId;
            }

            clothSystemChannel?.BroadcastPacket(new UnregisterClothSystemPacket() { ClothIds = clothIds });
        }

        private void Event_MapRegionLoaded(Vec2i mapCoord, IMapRegion region)
        {
            byte[]? data = region.GetModdata("clothSystems");
            
            if (data != null && data.Length != 0)
            {
                var rsystems = SerializerUtil.Deserialize<List<ClothSystem>>(data);

                foreach (var system in rsystems)
                {
                    // When the region first loads none of the chunks in it are loaded yet
                    system.Active = false;
                    system.Init(api, this);
                    clothSystems[system.ClothId] = system;
                }
            }
        }

        private TextCommandResult onClothTestClearServer(TextCommandCallingArgs args)
        {
            int cnt = clothSystems.Count;
            int[] clothids = clothSystems.Select(s => s.Value.ClothId).ToArray();
            if (clothids.Length > 0)
                clothSystemChannel?.BroadcastPacket(new UnregisterClothSystemPacket()
                    { ClothIds = clothids });
            clothSystems.Clear();
            return TextCommandResult.Success(cnt + " cloth sims removed");
        }
        
        private TextCommandResult onClothTestDeleteloaded(TextCommandCallingArgs args)
        {
            int cnt = 0;
            foreach (var val in sapi!.WorldManager.AllLoadedMapRegions)
            {
                val.Value.RemoveModdata("clothSystems");
                cnt++;
            }
            clothSystems.Clear();

            return TextCommandResult.Success($"Ok, deleted in {cnt} regions");
        }

        public void RegisterCloth(ClothSystem sys)
        {
            if (api.Side == EnumAppSide.Client) return;

            sys.ClothId = nextClothId++;
            clothSystems[sys.ClothId] = sys;

            sys.updateActiveState(EnumActiveStateChange.Default);

            clothSystemChannel?.BroadcastPacket(new ClothSystemPacket() { ClothSystems = new ClothSystem[] { sys } });
        }

        public void UnregisterCloth(int clothId)
        {
            if (sapi != null)
            {
                clothSystemChannel?.BroadcastPacket(new UnregisterClothSystemPacket() { ClothIds = new int[] { clothId } });
            }

            clothSystems.Remove(clothId);
        }

        private TextCommandResult onClothTestClear(TextCommandCallingArgs textCommandCallingArgs)
        {
            int cnt = clothSystems.Count;
            clothSystems.Clear();
            return TextCommandResult.Success(cnt + " cloth sims removed");
        }
        private TextCommandResult onClothTestCloth(TextCommandCallingArgs args)
        {
            float xsize = 0.5f + (float)api.World.Rand.NextDouble() * 3;
            float ysize = 0.5f + (float)api.World.Rand.NextDouble() * 3;
            float zsize = 0.5f + (float)api.World.Rand.NextDouble() * 3;
            var pos = args.Caller.Entity.Pos.AheadCopy(2).XYZ.Add(0, 1f, 0);

            ClothSystem sys = ClothSystem.CreateCloth(api, this, pos, pos.AddCopy(xsize, ysize, zsize));
            RegisterCloth(sys);
            sys.FirstPoint.PinTo(args.Caller.Entity, new Vec3f(0, 0.5f, 0));

            return TextCommandResult.Success();
        }
        private TextCommandResult onClothTestRope(TextCommandCallingArgs args)
        {
            float xsize = 0.5f + (float)api.World.Rand.NextDouble() * 3;
            float zsize = 0.5f + (float)api.World.Rand.NextDouble() * 3;
            xsize = 5;

            var rpos = args.Caller.Entity.Pos.AheadCopy(2).XYZ.Add(0, 1f, 0);

            ClothSystem sys = ClothSystem.CreateRope(api, this, rpos, rpos.AddCopy(xsize, zsize, xsize), null);
            sys.FirstPoint.PinTo(args.Caller.Entity, new Vec3f(0, 0.5f, 0));
            RegisterCloth(sys);

            return TextCommandResult.Success();
        }
    }

    public interface IRopeRippedListener
    {
        void OnRopeRipped(ClothSystem cs);
    }
}
