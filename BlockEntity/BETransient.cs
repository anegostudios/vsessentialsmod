using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public enum EnumTransientCondition
    {
        TimePassed,
        Temperature
    }

    public class TransientProperties
    {
        public EnumTransientCondition Condition = EnumTransientCondition.TimePassed;
        public float InGameHours = 24;
        public float WhenBelowTemperature = -999;
        public float WhenAboveTemperature = 999;

        public float ResetBelowTemperature = -999;
        public float StopBelowTemperature = -999;
        public string ConvertTo;
        public string ConvertFrom;
    }

    public class BlockEntityTransient : BlockEntity
    {
        double lastCheckAtTotalDays = 0;
        double transitionHoursLeft = -1;

        TransientProperties props;

        public virtual int CheckIntervalMs { get; set; } = 2000;

        long listenerId;

        double? transitionAtTotalDaysOld = null; // old v1.12 data format, here for backwards compatibility

        public string ConvertToOverride;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (Block.Attributes?["transientProps"].Exists != true) return;

            props = Block.Attributes["transientProps"].AsObject<TransientProperties>();
            if (props == null) return;


            if (transitionHoursLeft <= 0)
            {
                transitionHoursLeft = props.InGameHours;
            }

            if (api.Side == EnumAppSide.Server)
            {
                if (listenerId != 0)
                {
                    throw new InvalidOperationException("Initializing BETransient twice would create a memory and performance leak");
                }
                listenerId = RegisterGameTickListener(CheckTransition, CheckIntervalMs);

                if (transitionAtTotalDaysOld != null)
                {
                    transitionHoursLeft = ((double)transitionAtTotalDaysOld - Api.World.Calendar.TotalDays) * Api.World.Calendar.HoursPerDay;
                    lastCheckAtTotalDays = Api.World.Calendar.TotalDays;
                }
            }
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            lastCheckAtTotalDays = Api.World.Calendar.TotalDays;
        }

        public virtual void CheckTransition(float dt)
        {
            Block block = Api.World.BlockAccessor.GetBlock(Pos);
            if (block.Attributes == null)
            {
                Api.World.Logger.Error("BETransient exiting at {0} cannot find block attributes for {1}. Will stop transient timer", Pos, this.Block.Code.ToShortString());
                UnregisterGameTickListener(listenerId);
                return;
            }

            // In case this block was imported from another older world. In that case lastCheckAtTotalDays would be a future date.
            lastCheckAtTotalDays = Math.Min(lastCheckAtTotalDays, Api.World.Calendar.TotalDays);


            ClimateCondition baseClimate = Api.World.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.WorldGenValues);
            if (baseClimate == null) return;
            float baseTemperature = baseClimate.Temperature;

            float oneHour = 1f / Api.World.Calendar.HoursPerDay;
            while (Api.World.Calendar.TotalDays - lastCheckAtTotalDays > oneHour)
            {
                lastCheckAtTotalDays += oneHour;
                transitionHoursLeft -= 1f;

                baseClimate.Temperature = baseTemperature;
                ClimateCondition conds = Api.World.BlockAccessor.GetClimateAt(Pos, baseClimate, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, lastCheckAtTotalDays);

                if (props.Condition == EnumTransientCondition.Temperature)
                {
                    if (conds.Temperature < props.WhenBelowTemperature || conds.Temperature > props.WhenAboveTemperature)
                    {
                        tryTransition(props.ConvertTo);
                    }

                    continue;
                }


                bool reset = conds.Temperature < props.ResetBelowTemperature;
                bool stop = conds.Temperature < props.StopBelowTemperature;

                if (stop || reset)
                {
                    transitionHoursLeft += 1f;

                    if (reset)
                    {
                        transitionHoursLeft = props.InGameHours;
                    }

                    continue;
                }

                if (transitionHoursLeft <= 0) { 
                    tryTransition(ConvertToOverride ?? props.ConvertTo);
                    break;
                }
            }
        }

        public void tryTransition(string toCode) 
        { 
            Block block = Api.World.BlockAccessor.GetBlock(Pos);
            Block tblock;

            if (block.Attributes == null) return;

            string fromCode = props.ConvertFrom;
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

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            transitionHoursLeft = tree.GetDouble("transitionHoursLeft");

            if (tree.HasAttribute("transitionAtTotalDays")) // Pre 1.13 format
            {
                transitionAtTotalDaysOld = tree.GetDouble("transitionAtTotalDays");
            }

            lastCheckAtTotalDays = tree.GetDouble("lastCheckAtTotalDays");

            ConvertToOverride = tree.GetString("convertToOverride", null);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetDouble("transitionHoursLeft", transitionHoursLeft);
            tree.SetDouble("lastCheckAtTotalDays", lastCheckAtTotalDays);

            if (ConvertToOverride != null)
            {
                tree.SetString("convertToOverride", ConvertToOverride);
            }
        }


        public void SetPlaceTime(double totalHours)
        {
            float hours = props.InGameHours;

            transitionHoursLeft = hours + totalHours - Api.World.Calendar.TotalHours;
        }

        public bool IsDueTransition()
        {
            return transitionHoursLeft <= 0;
        }
    }
}
