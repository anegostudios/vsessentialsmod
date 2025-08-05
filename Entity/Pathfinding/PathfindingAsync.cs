using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

#nullable disable

namespace Vintagestory.Essentials
{
    public class PathfindingAsync : ModSystem, IAsyncServerSystem
    {
        protected ICoreServerAPI api;
        // This system has its own AStar classes to avoid potential cross-thread issues with others (e.g. Essentials.PathfindSystem)
        protected AStar astar_offthread;
        protected AStar astar_mainthread;
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

            api.Server.AddServerThread("ai-pathfinding", this);
        }


        public int OffThreadInterval()
        {
            return 5;
        }


        protected void OnMainThreadTick(float dt)
        {
            // Normally all queued tasks should be processed asynchronously in the separate thread ticks

            // The server main thread will also process tasks in parallel, if any are left in the queue here
            // This is called immediately prior to ticking entities on the main server thread
            // So normally all-but-one of the queued pathfinding tasks should be complete before the entities are next ticked
            // (there may be one task still in progress in the off-thread processor)

            int initialCount = PathfinderTasks.Count;
            if (initialCount > 1)   // If it's only one, leave it for the off-thread to do later - seems sometimes there is one slow one (150ms) left, no idea why, maybe lock contention?
            {
                api.World.FrameProfiler.Enter("ai-pathfinding-overflow " + initialCount);
                int maxCount = 1000;
                PathfinderTask task;
                while ((task = Next()) != null && maxCount-- > 0)
                {
                    task.waypoints = astar_mainthread.FindPathAsWaypoints(task.startBlockPos, task.targetBlockPos, task.maxFallHeight, task.stepHeight, task.collisionBox, task.searchDepth, task.mhdistanceTolerance);
                    task.Finished = true;
                    if (isShuttingDown) break;
                    if (api.World.FrameProfiler.Enabled) api.World.FrameProfiler.Mark("path d:" + task.searchDepth + " r:" + (task.waypoints == null ? "fail" : task.waypoints.Count.ToString()) + " s:" + task.startBlockPos + " e:" + task.targetBlockPos + " w:" + task.collisionBox.Width);
                }
                api.World.FrameProfiler.Leave();
            }
        }

        public void OnSeparateThreadTick()
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
                try
                {
                    task.waypoints = astar.FindPathAsWaypoints(task.startBlockPos, task.targetBlockPos, task.maxFallHeight, task.stepHeight, task.collisionBox, task.searchDepth, task.mhdistanceTolerance, task.CreatureType);
                } catch (Exception e)
                {
                    task.waypoints = null;
                    api.World.Logger.Error("Exception thrown during pathfinding. Will ignore. Exception: {0}", e.ToString());
                }

                task.Finished = true;

                if (isShuttingDown) break;
            }
        }

        /// <summary>
        /// Threadsafe way of dequeueing next task
        /// </summary>
        protected PathfinderTask Next()
        {
            if (!PathfinderTasks.TryDequeue(out PathfinderTask task)) task = null;
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

        public void ThreadDispose()
        {
            astar_offthread.Dispose();
            astar_offthread = null;
        }
    }
}
