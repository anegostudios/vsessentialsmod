using Newtonsoft.Json;
using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{
    // Lets make it a state machine.
    // Valid metal working states:
    // settled
    // quench
    // temper
    // overheat
    public class CollectibleBehaviorQuenchable : CollectibleBehavior
    {
        public float BreakChancePerQuench = 0.05f;
        public float TemperShatterMultiplier = 0.8f;
        public float TemperPowerMultiplier = 0.92f;
        protected string metalGroupCode;

        /// <summary>
        /// Returns true when still cooling
        /// </summary>
        /// <param name="world"></param>
        /// <param name="stack"></param>
        /// <param name="dt"></param>
        /// <param name="playSizzle"></param>
        /// <returns></returns>
        public static bool CoolToTemperature(IWorldAccessor world, ItemSlot slot, Vec3d pos, float dt, float targetTemperature, bool playSizzle = true)
        {
            float stackTemp = slot.Itemstack.Collectible.GetTemperature(world, slot.Itemstack);
            if (stackTemp <= targetTemperature) return false;

            var cbq = slot.Itemstack.Collectible.GetBehavior<CollectibleBehaviorQuenchable>();

            var nextTemperature = Math.Max(20, stackTemp - 5) * dt * 50;

            cbq?.IsGettingCooled(world, slot, pos, dt, nextTemperature);
            if (slot.Empty) return false;
            slot.Itemstack.Collectible.SetTemperature(world, slot.Itemstack, nextTemperature);

            float tempDiff = stackTemp - targetTemperature;

            if (tempDiff > 90)
            {
                double width = 0.0; // EntityItem SelectionBox.XSize;
                Entity.SplashParticleProps.BasePos.Set(pos.X - width / 2, pos.Y - 0.75, pos.Z - width / 2);
                Entity.SplashParticleProps.AddVelocity.Set(0, 0, 0);
                Entity.SplashParticleProps.QuantityMul = 0.1f;
                world.SpawnParticles(Entity.SplashParticleProps);
            }

            long lastPlayedSizzlesTotalMs = slot.Itemstack.TempAttributes.GetLong("lastPlayedSizzlesTotalMs", -99999);

            if (playSizzle && tempDiff > 200 && world.Side == EnumAppSide.Client && world.ElapsedMilliseconds - lastPlayedSizzlesTotalMs > 10000)
            {
                world.PlaySoundAt(new AssetLocation("sounds/sizzle"), pos.X, pos.Y, pos.Z, null);
                slot.Itemstack.TempAttributes.SetLong("lastPlayedSizzlesTotalMs", world.ElapsedMilliseconds);
            }

            return true;
        }


        public CollectibleBehaviorQuenchable(CollectibleObject collObj) : base(collObj) { }



        public override void OnLoaded(ICoreAPI api)
        {
            IAsset asset = api.Assets.Get("worldproperties/block/metal.json");
            var metaltypes = asset.ToObject<MetalWorldProperties>();

            
            string metalCode = collObj.Variant[metalGroupCode];
            if (metalCode == null)
            {
                api.Logger.Warning("Collectible {0} has quenchable property, but cannot get metal code from metal group code \"{1}\"", collObj.Code, metalGroupCode);
                return;
            }

            metalProps = metaltypes.Variants.FirstOrDefault(p => p.Code.ToShortString().Equals(metalCode));
            if (metalProps == null)
            {
                api.Logger.Warning("Collectible {0} has quenchable property, but cannot find metal {1} in metal.json world properties file.", collObj.Code, metalCode);
            };
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);

            metalGroupCode = properties["metalVariantgroupCode"].AsString("metal");
        }

        MetalPropertyVariant metalProps;


        public string GetState(ItemStack itemStack) => itemStack.Attributes.GetString("metalworkingstate", "settled");
        public void SetState(IWorldAccessor world, ItemStack itemStack, string state)
        {
            if (GetState(itemStack) == state) return;
            itemStack.Attributes.SetString("metalworkingstate", state);
            itemStack.Attributes.SetDouble("statechangetotalhours", world.Calendar.ElapsedHours);
        }
        public float GetShatterChance(IWorldAccessor world, ItemStack itemstack) => itemstack.Attributes.GetFloat("shatterchance", BreakChancePerQuench);
        public void SetShatterChance(IWorldAccessor world, ItemStack itemstack, float value) => itemstack.Attributes.SetFloat("shatterchance", value);
        public float GetPowerValue(IWorldAccessor world, ItemStack itemstack) => itemstack.Attributes.GetFloat("powervalue", 0);
        public void SetPowerValue(IWorldAccessor world, ItemStack itemstack, float value) => itemstack.Attributes.SetFloat("powervalue", value);
        public float GetDurationBonus(IWorldAccessor world, ItemStack itemstack) => itemstack.Attributes.GetFloat("durationbonus", 0);
        public void SetDurationBonus(IWorldAccessor world, ItemStack itemstack, float value) => itemstack.Attributes.SetFloat("durationbonus", value);


        public override void AfterGetTemperature(IWorldAccessor world, ItemStack itemstack, float temperature, ref EnumHandling handling)
        {
            if (metalProps == null) return;

            string currentState = GetState(itemstack);
            trySettleWorkItem(world, itemstack, temperature, currentState);
        }

        public override void SetTemperature(IWorldAccessor world, ItemStack itemstack, float temperature, bool delayCooldown, ref EnumHandling handling)
        {
            if (metalProps == null) return;

            bool nowQuenching = temperature > metalProps.quenchMinTemp && temperature < metalProps.quenchMaxTemp;
            bool nowTempering = temperature > metalProps.temperMinTemp && temperature < metalProps.temperMaxTemp;

            string currentState = GetState(itemstack);

            if (currentState == "overheat")
            {
                if (temperature < metalProps.settledTemperature) SetState(world, itemstack, "settled");
                return;
            }

            // If we're above quench temperature we're not doing anything useful anymore
            if (temperature > metalProps.quenchMaxTemp)
            {
                SetState(world, itemstack, "overheat");
            }
            else if (temperature > metalProps.quenchMinTemp)
            {
                SetState(world, itemstack, "quench");
            }
            else if (temperature > metalProps.temperMinTemp)
            {
                if (temperature > metalProps.temperMaxTemp && currentState == "temper") SetState(world, itemstack, "settled");
                if (currentState == "settled") SetState(world, itemstack, "temper");
            }

            trySettleWorkItem(world, itemstack, temperature, currentState);
        }

        private void IsGettingCooled(IWorldAccessor world, ItemSlot slot, Vec3d pos, float dt, float temperature)
        {
            if (world.Side == EnumAppSide.Client) return;

            string currentState = GetState(slot.Itemstack);
            if ((currentState == "quench" || currentState == "overheat") && temperature < collObj.GetTemperature(world, slot.Itemstack) - 2)
            {
                float shatterChance = GetShatterChance(world, slot.Itemstack);

                if (!slot.Itemstack.TempAttributes.HasAttribute("willbreak"))
                {
                    slot.Itemstack.TempAttributes.SetBool("willbreak", world.Rand.NextDouble() < shatterChance);
                }

                bool willbreak = slot.Itemstack.TempAttributes.GetBool("willbreak");

                if (willbreak && (temperature <= metalProps.settledTemperature || world.Rand.NextDouble() < 0.05))
                {
                    world.PlaySoundAt(new AssetLocation("sounds/effect/toolbreak"), pos.X, pos.Y, pos.Z, null, false, 16);
                    world.SpawnCubeParticles(pos, slot.Itemstack, 0.25f, 30, 1, null);
                    slot.Itemstack = null;
                    slot.MarkDirty();
                }
            }
        }


        private void trySettleWorkItem(IWorldAccessor world, ItemStack itemstack, float temperature, string currentState)
        {
            if (currentState == "settled") return;

            if (temperature <= metalProps.settledTemperature)
            {
                double hoursPassed = world.Calendar.ElapsedHours - itemstack.Attributes.GetDouble("statechangetotalhours", -999);

                if (currentState == "quench" && hoursPassed < 0.25)
                {
                    applyQuenchedStats(world, itemstack);
                }

                if (currentState == "temper" && hoursPassed > 1)
                {
                    applyTemperedStats(world, itemstack);
                }

                SetState(world, itemstack, "settled");

                itemstack.TempAttributes.RemoveAttribute("willbreak");
            }
        }


        private void applyTemperedStats(IWorldAccessor world, ItemStack itemstack)
        {
            int temperIteration = itemstack.Attributes.GetInt("temperIteration", 0);

            // Diminishing returns curve: 1/(1+x*0.05)
            // More generous than the quenching curve
            float effectiveness = 1f / (1 + temperIteration * 0.05f);

            float newShatterChance = GetShatterChance(world, itemstack) * GameMath.Mix(1, TemperShatterMultiplier, effectiveness);
            float newPowerValue = GetPowerValue(world, itemstack) * GameMath.Mix(1, TemperPowerMultiplier, effectiveness);

            SetShatterChance(world, itemstack, newShatterChance);
            SetPowerValue(world, itemstack, newPowerValue);

            itemstack.Attributes.SetInt("temperIteration", temperIteration + 1);
            applyBuffs(itemstack);
        }


        private void applyQuenchedStats(IWorldAccessor world, ItemStack itemstack)
        {
            bool clayCovered = itemstack.Attributes.GetBool("clayCovered", false);

            int quenchIteration = itemstack.Attributes.GetInt("quenchIteration", 0);

            float shatterChance = GetShatterChance(world, itemstack);
            SetShatterChance(world, itemstack, shatterChance + BreakChancePerQuench);

            // Diminishing returns curve: 0.1/(1+x*0.2)
            // https://pfortuny.net/fooplot.com/#W3sidHlwZSI6MCwiZXEiOiIwLjEvKDEreCowLjIpIiwiY29sb3IiOiIjMDAwMDAwIn0seyJ0eXBlIjoxMDAwLCJ3aW5kb3ciOlsiMCIsIjE1IiwiMCIsIjAuMSJdLCJzaXplIjpbNjQ4LDM5OF19XQ--

            if (clayCovered)
            {
                var dbonus = GetDurationBonus(world, itemstack);
                SetDurationBonus(world, itemstack, dbonus + 0.2f / (1 + quenchIteration * 0.2f));
            } else
            {
                var powervalue = GetPowerValue(world, itemstack);
                SetPowerValue(world, itemstack, powervalue + 0.1f / (1 + quenchIteration * 0.2f));
            }

            itemstack.Attributes.SetInt("quenchIteration", quenchIteration + 1);
            itemstack.Attributes.SetBool("clayCovered", false);
            applyBuffs(itemstack);
        }

        private void applyBuffs(ItemStack itemStack)
        {
            var cbb = collObj.GetBehavior<CollectibleBehaviorBuffable>();
            if (cbb == null) return;
            cbb.applyQuenchableBuffs(itemStack, itemStack);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            dsc.AppendLine(Lang.Get("itemstack-quenchable", metalProps.quenchMinTemp, metalProps.quenchMaxTemp));
            dsc.AppendLine(Lang.Get("itemstack-temperable", metalProps.temperMinTemp, metalProps.temperMaxTemp));

            bool clayCovered = inSlot.Itemstack.Attributes.GetBool("clayCovered", false);
            if (clayCovered)
            {
                dsc.AppendLine(Lang.Get("itemstack-claycovered"));
            }

            int timesQuenched = inSlot.Itemstack.Attributes.GetInt("quenchIteration", 0);
            if (timesQuenched > 0)
            {
                int timesTempered = inSlot.Itemstack.Attributes.GetInt("temperIteration", 0);
                if (timesTempered > 0)
                {
                    dsc.AppendLine(Lang.Get("Times tempered: {0}", timesTempered));
                }
                dsc.AppendLine(Lang.Get("Times quenched: {0}", timesQuenched));
                dsc.AppendLine(Lang.Get("Quench shatter chance: {0:0.#%}", GetShatterChance(world, inSlot.Itemstack)));
                dsc.AppendLine(Lang.Get("Power gain: {0:\\+0.#%;0.#%}", GetPowerValue(world, inSlot.Itemstack)));
                dsc.AppendLine(Lang.Get("Durability gain: {0:\\+0.#%;0.#%}", GetDurationBonus(world, inSlot.Itemstack)));
            }

            if ((world.Api as ICoreClientAPI).Settings.Bool["extendedDebugInfo"])
            {
                dsc.AppendLine(string.Format("<font color=\"#ccc\">workitemstate: {0}</font>", GetState(inSlot.Itemstack)));
            }
        }




        public class MetalPropertyVariant : WorldPropertyVariant
        {
            [JsonProperty]
            public int quenchMinTemp;
            [JsonProperty]
            public int quenchMaxTemp;
            [JsonProperty]
            public int temperMinTemp;
            [JsonProperty]
            public int temperMaxTemp;
            [JsonProperty]
            public int settledTemperature = 100;
        }

        public class MetalWorldProperties : WorldProperty<MetalPropertyVariant>
        {

        }


    }




}
