using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Essentials;

namespace Vintagestory.Essentials
{
    public class PathfindingAsync : ModSystem
    {
        protected ICoreServerAPI api;
        // This system has its own AStar classes to avoid potential cross-thread issues with others (e.g. Essentials.PathfindSystem)
        protected AStar astar_offthread;
        protected AStar astar_mainthread;
        protected Thread pathfindThread;
        protected bool isShuttingDown;
        public ConcurrentQueue<PathfinderTask> PathfinderTasks = new ConcurrentQueue<PathfinderTask>();
        protected readonly Stopwatch totalTime = new Stopwatch();
        protected long lastTickTimeMs;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            this.api = api;
            astar_offthread = new AStar(api);
            astar_mainthread = new AStar(api);

            api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, () => isShuttingDown = true);
            api.Event.RegisterGameTickListener(OnMainThreadTick, 20);

            pathfindThread = new Thread(new ThreadStart(SeparateThreadLoop));
            pathfindThread.IsBackground = true;
            pathfindThread.Start();
        }


        protected int OffThreadInterval()
        {
            return 5;
        }


        protected void SeparateThreadLoop()
        {
            // This is basically the same logic as a ServerThread, but without the complexity of multiple serversystems
            totalTime.Start();
            long elapsedMs;

            try
            {
                while(!isShuttingDown)
                {
                    elapsedMs = totalTime.ElapsedMilliseconds;
                    if (elapsedMs - lastTickTimeMs >= OffThreadInterval())
                    {
                        lastTickTimeMs = elapsedMs;
                        this.OnSeparateThreadTick();
                    }

                    Thread.Sleep(1);
                }

            }
            catch (ThreadAbortException) { } // ignore these because we can.
            catch (Exception e)
            {
                api.Logger.Fatal("Caught unhandled exception in pathfinding thread.");
                api.Logger.Fatal(e.ToString());
                api.Event.EnqueueMainThreadTask(() =>
                {
                    api.Server.ShutDown();
                }, "pathfinding");

            }

            astar_offthread.Dispose();
            astar_offthread = null;
        }


        protected void OnMainThreadTick(float dt)
        {
            // Normally all queued tasks should be processed asynchronously in the separate thread ticks

            // The server main thread will also process tasks in parallel, if any are left in the queue here
            // This is called immediately prior to ticking entities on the main server thread
            // So normally all-but-one of the queued pathfinding tasks should be complete before the entities are next ticked
            // (there may be one task still in progress in the off-thread processor)

            int initialCount = PathfinderTasks.Count;
            ProcessQueue(astar_mainthread, 1000);
            if (initialCount > 0) api.World.FrameProfiler.Mark("ai-pathfinding-overflow " + initialCount + " " + PathfinderTasks.Count);
        }

        protected void OnSeparateThreadTick()
        {
            ProcessQueue(astar_offthread, 100);
        }

        /// <summary>
        /// Try to process the whole queue.  Can be called on either the main thread or the separate thread
        /// <br/> Note that if the queue is very long, the maxCount prevents the separate thread from processing indefinitely, otherwise it would saturate 1 CPU core indefinitely
        /// </summary>
        /// <param name="astar"></param>
        public void ProcessQueue(AStar astar, int maxCount)
        {
            PathfinderTask task;
            while ((task = Next()) != null && maxCount-- > 0)
            {
                task.waypoints = astar.FindPathAsWaypoints(task.startBlockPos, task.targetBlockPos, task.maxFallHeight, task.stepHeight, task.collisionBox, task.searchDepth, task.mhdistanceTolerance);
                task.Finished = true;

                if (isShuttingDown) break;
            }
        }

        /// <summary>
        /// Threadsafe way of dequeueing next task
        /// </summary>
        protected PathfinderTask Next()
        {
            PathfinderTask task = null;
            if (!PathfinderTasks.TryDequeue(out task)) task = null;
            return task;
        }


        public void EnqueuePathfinderTask(PathfinderTask task)
        {
            PathfinderTasks.Enqueue(task);
        }


        public override void Dispose()
        {
            astar_mainthread?.Dispose();   // astar_mainthread will be null clientside, as this ModSystem is only started on servers
            astar_mainthread = null;
        }
    }
}
