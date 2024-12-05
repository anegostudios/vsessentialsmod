using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{

    public class LeafBlockDecay : ModSystem
    {
        private ICoreServerAPI sapi;
        private HashSet<BlockPos> checkDecayQueue = new HashSet<BlockPos>();
        public static object checkDecayLock = new object();

        private HashSet<BlockPos> performDecayQueue = new HashSet<BlockPos>();
        public static object performDecayLock = new object();

        private CheckDecayThread checkDecayThread;

        public static int leafRemovalInterval = 1000;


        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            api.Event.SaveGameLoaded += onSaveGameLoaded;
            api.Event.GameWorldSave += onGameGettingSaved;
            api.Event.RegisterEventBusListener(onLeafDecayEventReceived, 0.5, "testForDecay");
            api.Event.RegisterGameTickListener(processReadyToDecayQueue, leafRemovalInterval);


            sapi.ChatCommands.GetOrCreate("debug")
                .BeginSubCommand("leafdecaydebug")
                    .WithDescription("Shows leaf decay stats")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(_ =>
                        TextCommandResult.Success("Queue sizes: pdq: " + performDecayQueue.Count + " / cdq: " + checkDecayQueue.Count))
                .EndSubCommand();
        }

        private void processReadyToDecayQueue(float dt)
        {
            if (performDecayQueue.Count == 0) return;

            BlockPos pos = null;
            lock (performDecayLock)
            {
                pos = performDecayQueue.First<BlockPos>();
                performDecayQueue.Remove(pos);
            }

            doDecay(pos);
        }

        private void onLeafDecayEventReceived(string eventName, ref EnumHandling handling, IAttribute data)
        {
            if (checkDecayThread != null)
            {
                TreeAttribute tree = data as TreeAttribute;
                BlockPos pos = new BlockPos(tree.GetInt("x"), tree.GetInt("y"), tree.GetInt("z"));
                queueNeighborsForCheckDecay(pos);
            }
        }

        private void queueNeighborsForCheckDecay(BlockPos pos)
        {
            lock (checkDecayLock)
            {
                for (int i = 0; i < Vec3i.DirectAndIndirectNeighbours.Length; i++)
                {
                    Vec3i vec = Vec3i.DirectAndIndirectNeighbours[i];
                    Block block = sapi.World.BlockAccessor.GetBlockRaw(pos.X + vec.X, pos.InternalY + vec.Y, pos.Z + vec.Z);
                    if (block.Id == 0) continue;

                    if (canDecay(block))
                    {
                        checkDecayQueue.Add(pos.AddCopy(vec));
                    }
                }
            }
        }
        

        private void doDecay(BlockPos pos)
        {
            Block block = sapi.World.BlockAccessor.GetBlock(pos);

            if (canDecay(block)) // In case a non leaf block has been placed recently
            {
                sapi.World.BlockAccessor.SetBlock(0, pos);

                for (int i = 0; i < BlockFacing.NumberOfFaces; i++)
                {
                    sapi.World.BlockAccessor.MarkBlockDirty(pos.AddCopy(BlockFacing.ALLFACES[i]));
                }

                queueNeighborsForCheckDecay(pos);
            }
        }

        private void onGameGettingSaved()
        {
            lock (checkDecayLock)
            {
                sapi.WorldManager.SaveGame.StoreData("checkDecayQueue", SerializerUtil.Serialize(checkDecayQueue));
                sapi.WorldManager.SaveGame.StoreData("performDecayQueue", SerializerUtil.Serialize(performDecayQueue));
            }
        }

        private void onSaveGameLoaded()
        {
            sapi.Logger.Debug("Loading leaf block decay system");
            checkDecayQueue = deserializeQueue("checkDecayQueue");
            performDecayQueue = deserializeQueue("performDecayQueue");
            checkDecayThread = new CheckDecayThread(sapi);
            checkDecayThread.Start(checkDecayQueue, performDecayQueue);
            sapi.Logger.Debug("Finished loading leaf block decay system");
        }

        private HashSet<BlockPos> deserializeQueue(string name)
        {      
            try
            {
                byte[] data = sapi.WorldManager.SaveGame.GetData(name);
                if (data != null)
                {
                    return SerializerUtil.Deserialize<HashSet<BlockPos>>(data);
                }
            }
            catch (Exception e)
            {
                sapi.World.Logger.Error("Failed loading LeafBlockDecay.{0}. Resetting. Exception:", name);
                sapi.World.Logger.Error(e);
            }
            return new HashSet<BlockPos>();
        }


        public static bool canDecay(Block block)
        {
            return block.BlockMaterial == EnumBlockMaterial.Leaves && block.Attributes?.IsTrue("canDecay") == true;
        }

        public static bool preventsDecay(Block block)
        {
            return block.Id != 0 && block.Attributes?.IsTrue("preventsDecay") == true;
        }







        class CheckDecayThread : IAsyncServerSystem
        {
            public static int leafDecayCheckTickInterval = 10;
            


            public bool Stopping { get; set; }
            private HashSet<BlockPos> checkDecay;
            private HashSet<BlockPos> performDecay;
            private ICoreServerAPI sapi;

            public CheckDecayThread(ICoreServerAPI sapi)
            {
                this.sapi = sapi;
            }

            public void Start(HashSet<BlockPos> checkDecay, HashSet<BlockPos> performDecay)
            {
                this.checkDecay = checkDecay;
                this.performDecay = performDecay;

                sapi.Server.AddServerThread("CheckLeafDecay", this);
            }

            public int OffThreadInterval()
            {
                return leafDecayCheckTickInterval;
            }

            public void OnSeparateThreadTick()
            {
                for (int i = 0; i < 100; i++)
                {
                    if (checkDecay.Count == 0) return;

                    BlockPos pos = null;
                    lock (checkDecayLock)
                    {
                        pos = checkDecay.First<BlockPos>();
                        checkDecay.Remove(pos);
                    }

                    if (shouldDecay(pos))
                    {
                        lock (performDecayLock)
                        {
                            performDecay.Add(pos);
                        }
                    }
                }
            }

            public void ThreadDispose()
            {
                lock (checkDecayLock)
                {
                    checkDecay.Clear();
                }

                lock (performDecayLock)
                {
                    performDecay.Clear();
                }
            }


            private bool shouldDecay(BlockPos startPos)
            {
                Queue<Vec4i> bfsQueue = new Queue<Vec4i>();
                HashSet<BlockPos> checkedPositions = new HashSet<BlockPos>();

                IBlockAccessor blockAccessor = sapi.World.BlockAccessor;

                Block block = blockAccessor.GetBlock(startPos);

                
                if (canDecay(block))
                {
                    bfsQueue.Enqueue(new Vec4i(startPos, 2));
                    checkedPositions.Add(startPos);
                } else
                {
                    return false;
                }
                

                while (bfsQueue.Count > 0)
                {
                    if (checkedPositions.Count > 600)
                    {
                        return false;
                    }

                    Vec4i pos = bfsQueue.Dequeue();

                    for (int i = 0; i < Vec3i.DirectAndIndirectNeighbours.Length; i++)
                    {
                        Vec3i facing = Vec3i.DirectAndIndirectNeighbours[i];
                        BlockPos curPos = new BlockPos(pos.X + facing.X, pos.Y + facing.Y, pos.Z + facing.Z);
                        if (checkedPositions.Contains(curPos)) continue;
                        checkedPositions.Add(curPos);

                        block = blockAccessor.GetBlock(curPos);

                        // Let's just say a leaves block can grow as long as its connected to 
                        // something woody or something fertile
                        if (preventsDecay(block))
                        {
                            return false;
                        }


                        int diffx = (curPos.X - startPos.X);
                        int diffy = (curPos.Y - startPos.Y);
                        int diffz = (curPos.Z - startPos.Z);

                        // Only test within a 6x6x6
                        if (Math.Abs(diffx) > 4 || Math.Abs(diffy) > 4 || Math.Abs(diffz) > 4)
                        {
                            if (block.Id != 0)
                            {
                                return false;
                            }
                            continue;
                        }
                        
                        if (canDecay(block))
                        {
                            bfsQueue.Enqueue(new Vec4i(curPos, 0));
                        }
                    }
                }


                return true;
            }

        }

    }
}
