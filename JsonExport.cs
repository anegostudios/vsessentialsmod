using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

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

            api.RegisterCommand("jsonexport", "Export items and blocks as json files", "", CmdExport, Privilege.controlserver);
           
            this.api = api;
        }

        private void CmdExport(IServerPlayer player, int groupId, CmdArgs args)
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
        }
    }
}
