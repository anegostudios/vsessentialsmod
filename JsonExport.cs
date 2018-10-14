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
        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Server;
        }

        ICoreServerAPI api;
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

           // api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, onRunGame);

            this.api = api;
        }

        private void onRunGame()
        {
            
            StringBuilder sql = new StringBuilder();
            sql.Append("[");

            for (int i = 0; i < api.World.Blocks.Length; i++)
            {
                Block block = api.World.Blocks[i];

                if (block == null || block.Code == null) continue;

                if (i > 0) sql.Append(",");
                sql.Append("{");
                sql.Append(string.Format("\"name\": \"{0}\", ", new ItemStack(block).GetName()));
                sql.Append(string.Format("\"code\": \"{0}\", ", block.Code));
                sql.Append(string.Format("\"material\": \"{0}\", ", block.BlockMaterial));
                sql.Append(string.Format("\"shape\": \"{0}\", ", block.Shape.Base.Path));
                sql.Append(string.Format("\"tool\": \"{0}\"", block.Tool));
                sql.Append("}");
            }

            sql.Append("]");

            File.WriteAllText("blocks.json", sql.ToString());
        }
    }
}
