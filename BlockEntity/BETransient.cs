using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public class BlockEntityTransient : BlockEntity
    {
        double transitionAtTotalDays = -1;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (transitionAtTotalDays <= 0)
            {
                float hours = Block.Attributes["inGameHours"].AsFloat(24);
                transitionAtTotalDays = api.World.Calendar.TotalDays + hours / 24;
            }

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(CheckTransition, 2000);
            }
        }

        public void CheckTransition(float dt)
        {
            if (transitionAtTotalDays > Api.World.Calendar.TotalDays) return;

            Block block = Api.World.BlockAccessor.GetBlock(Pos);
            Block tblock;

            if (block.Attributes == null) return;

            string fromCode = block.Attributes["convertFrom"].AsString();
            string toCode = block.Attributes["convertTo"].AsString();
            if (fromCode == null || toCode == null) return;

            if (fromCode.IndexOf(":") == -1) fromCode = block.Code.Domain + ":" + fromCode;
            if (toCode.IndexOf(":") == -1) toCode = block.Code.Domain + ":" + toCode;


            if (fromCode == null || !toCode.Contains("*"))
            {
                tblock = Api.World.GetBlock(new AssetLocation(toCode));
                if (tblock == null) return;

                Api.World.BlockAccessor.SetBlock(tblock.BlockId, Pos);
                return;
            }
            
            AssetLocation blockCode = block.WildCardReplace(
                new AssetLocation(fromCode), 
                new AssetLocation(toCode)
            );

            tblock = Api.World.GetBlock(blockCode);
            if (tblock == null) return;

            Api.World.BlockAccessor.SetBlock(tblock.BlockId, Pos);
        }

        public override void FromTreeAtributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAtributes(tree, worldForResolving);

            transitionAtTotalDays = tree.GetDouble("transitionAtTotalDays");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetDouble("transitionAtTotalDays", transitionAtTotalDays);
        }

        public bool WasPlacedAtTotalHours(double totalHoursUntilPlace)
        {
            Block block = Api.World.BlockAccessor.GetBlock(Pos);
            float hours = block.Attributes["inGameHours"].AsFloat(24);
            transitionAtTotalDays = totalHoursUntilPlace / Api.World.Calendar.HoursPerDay + hours / 24;

            return transitionAtTotalDays > Api.World.Calendar.TotalDays;
        }
    }
}
