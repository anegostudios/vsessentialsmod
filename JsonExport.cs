using System;
using System.IO;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

#nullable disable

namespace Vintagestory.ServerMods
{
    public class JsonExport : ModSystem
    {
        ICoreServerAPI api;

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Server;
        }

        
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            api.ChatCommands.GetOrCreate("dev")
                    .BeginSubCommand("jsonexport")
                    .WithDescription("Export items and blocks as json files")
                    .RequiresPrivilege(Privilege.controlserver)
                    .HandleWith(CmdExport)
                .EndSubCommand();
           
            this.api = api;
        }

        private TextCommandResult CmdExport(TextCommandCallingArgs textCommandCallingArgs)
        {   
            StringBuilder sql = new StringBuilder();
            sql.Append("[");
            int cnt = 0;
            for (int i = 0; i < api.World.Blocks.Count; i++)
            {
                Block block = api.World.Blocks[i];

                if (block == null || block.Code == null) continue;

                if (cnt > 0) sql.Append(",");
                sql.Append("{");
                sql.Append(string.Format("\"name\": \"{0}\", ", new ItemStack(block).GetName()));
                sql.Append(string.Format("\"code\": \"{0}\", ", block.Code));
                sql.Append(string.Format("\"material\": \"{0}\", ", block.BlockMaterial));
                sql.Append(string.Format("\"shape\": \"{0}\", ", block.Shape.Base.Path));
                sql.Append(string.Format("\"tool\": \"{0}\"", block.Tool));
                sql.Append("}");
                cnt++;
            }

            sql.Append("]");

            File.WriteAllText("blocks.json", sql.ToString());



            sql = new StringBuilder();
            sql.Append("[");
            cnt = 0;

            for (int i = 0; i < api.World.Items.Count; i++)
            {
                Item item = api.World.Items[i];

                if (item == null || item.Code == null) continue;

                if (cnt > 0) sql.Append(",");
                sql.Append("{");
                sql.Append(string.Format("\"name\": \"{0}\", ", new ItemStack(item).GetName()));
                sql.Append(string.Format("\"code\": \"{0}\", ", item.Code));
                sql.Append(string.Format("\"shape\": \"{0}\", ", item.Shape?.Base?.Path));
                sql.Append(string.Format("\"tool\": \"{0}\"", item.Tool));
                sql.Append("}");
                cnt++;
            }

            sql.Append("]");

            File.WriteAllText("items.json", sql.ToString());
            
            return TextCommandResult.Success("All Blocks and Items written to block.json and item.json in " + AppDomain.CurrentDomain.BaseDirectory);
        }
    }
}
