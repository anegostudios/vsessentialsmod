using System.Collections.Generic;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.Essentials
{
    public class PathFindDebug : ModSystem
    {
        BlockPos start;
        BlockPos end;
        ICoreServerAPI sapi;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Server;
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;

            api.ChatCommands
                .GetOrCreate("debug")
                .BeginSub("astar")
                    .WithDesc("A* path finding debug testing tool")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(api.ChatCommands.Parsers.WordRange("command", "start", "end", "bench", "clear"))
                    .HandleWith(onAstarCmd)
                .EndSub()
            ;
        }

        private TextCommandResult onAstarCmd(TextCommandCallingArgs args)
        {
            string subcmd = (string)args[0];
            var player = args.Caller.Player;

            BlockPos plrPos = player.Entity.ServerPos.XYZ.AsBlockPos;
            PathfindSystem pfs = sapi.ModLoader.GetModSystem<PathfindSystem>();

            Cuboidf narrow = new Cuboidf(-0.4f, 0, -0.4f, 0.4f, 1.5f, 0.4f);
            Cuboidf narrower = new Cuboidf(-0.2f, 0, -0.2f, 0.2f, 1.5f, 0.2f);
            Cuboidf wide = new Cuboidf(-0.6f, 0, -0.6f, 0.6f, 1.5f, 0.6f);

            Cuboidf collbox = narrow;
            int maxFallHeight = 3;
            float stepHeight = 1.01f;


            switch (subcmd)
            {
                case "start":
                    start = plrPos.Copy();
                    sapi.World.HighlightBlocks(player, 26, new List<BlockPos>() { start }, new List<int>() { ColorUtil.ColorFromRgba(255, 255, 0, 128) }, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
                    break;
                case "end":
                    end = plrPos.Copy();
                    sapi.World.HighlightBlocks(player, 27, new List<BlockPos>() { end }, new List<int>() { ColorUtil.ColorFromRgba(255, 0, 255, 128) }, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
                    break;
                case "bench":
                    if (start == null || end == null) return TextCommandResult.Error("Start/End not set");

                    Stopwatch sw = new Stopwatch();
                    sw.Start();

                    for (int i = 0; i < 15; i++)
                    {
                        List<PathNode> nodes = pfs.FindPath(start, end, maxFallHeight, stepHeight, collbox);
                    }

                    sw.Stop();
                    float timeMs = (float)sw.ElapsedMilliseconds / 15f;

                    return TextCommandResult.Success(string.Format("15 searches average: {0} ms", (int)timeMs));

                case "clear":
                    start = null;
                    end = null;

                    sapi.World.HighlightBlocks(player, (int)EnumHighlightSlot.Spawner, new List<BlockPos>(), EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
                    sapi.World.HighlightBlocks(player, 26, new List<BlockPos>(), EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
                    sapi.World.HighlightBlocks(player, 27, new List<BlockPos>(), EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
                    break;
            }

            if (start == null || end == null)
            {
                sapi.World.HighlightBlocks(player, (int)EnumHighlightSlot.Spawner, new List<BlockPos>(), EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
            }
            if (start != null && end != null)
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();

                List<PathNode> nodes = pfs.FindPath(start, end, maxFallHeight, stepHeight, collbox);
                sw.Stop();
                int timeMs = (int)sw.ElapsedMilliseconds;

                var message = string.Format("Search took {0} ms, {1} nodes checked", timeMs, pfs.astar.NodesChecked);

                if (nodes == null)
                {
                    sapi.World.HighlightBlocks(player, (int)EnumHighlightSlot.Spawner, new List<BlockPos>(), EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
                    sapi.World.HighlightBlocks(player, (int)EnumHighlightSlot.LandClaim, new List<BlockPos>(), EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
                    return TextCommandResult.Success(message + "\n" + "No path found");
                }

                List<BlockPos> poses = new List<BlockPos>();
                foreach (var node in nodes)
                {
                    poses.Add(node);
                }

                sapi.World.HighlightBlocks(player, (int)EnumHighlightSlot.Spawner, poses, new List<int>() { ColorUtil.ColorFromRgba(128, 128, 128, 30) }, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);


                List<Vec3d> wps = pfs.ToWaypoints(nodes);
                poses = new List<BlockPos>();
                foreach (var node in wps)
                {
                    poses.Add(node.AsBlockPos);
                }

                sapi.World.HighlightBlocks(player, (int)EnumHighlightSlot.LandClaim, poses, new List<int>() { ColorUtil.ColorFromRgba(128, 0, 0, 100) }, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);

                return TextCommandResult.Success(message);
            }

            return TextCommandResult.Success();
        }
    }
}
