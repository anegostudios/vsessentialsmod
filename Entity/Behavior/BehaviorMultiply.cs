using Newtonsoft.Json;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorMultiply : EntityBehaviorMultiplyBase
    {
        JsonObject typeAttributes;
        long callbackId;
        AssetLocation[] spawnEntityCodes;

        internal float PregnancyDays
        {
            get { return typeAttributes["pregnancyDays"].AsFloat(3f); }
        }

        internal string RequiresNearbyEntityCode
        {
            get { return typeAttributes["requiresNearbyEntityCode"].AsString(""); }
        }

        internal float RequiresNearbyEntityRange
        {
            get { return typeAttributes["requiresNearbyEntityRange"].AsFloat(5); }
        }

        /*internal int GrowthCapQuantity
        {
            get { return attributes["growthCapQuantity"].AsInt(10); }
        }

        internal float GrowthCapRange
        {
            get { return attributes["growthCapRange"].AsFloat(10); }
        }

        internal AssetLocation[] GrowthCapEntityCodes
        {
            get { return AssetLocation.toLocations(attributes["growthCapEntityCodes"].AsStringArray(new string[0])); }
        }*/

        public float SpawnQuantityMin
        {
            get { return typeAttributes["spawnQuantityMin"].AsFloat(1); }
        }
        public float SpawnQuantityMax
        {
            get { return typeAttributes["spawnQuantityMax"].AsFloat(2); }
        }


        public double TotalDaysLastBirth
        {
            get { return multiplyTree.GetDouble("totalDaysLastBirth", -9999); }
            set { multiplyTree.SetDouble("totalDaysLastBirth", value); }
        }

        public double TotalDaysPregnancyStart
        {
            get { return multiplyTree.GetDouble("totalDaysPregnancyStart"); }
            set { multiplyTree.SetDouble("totalDaysPregnancyStart", value); }
        }

        public bool IsPregnant
        {
            get { return multiplyTree.GetBool("isPregnant"); }
            set { multiplyTree.SetBool("isPregnant", value); }
        }

        bool eatAnyway = false;

        public override bool ShouldEat
        {
            get
            {
                return 
                    eatAnyway || 
                    (
                        !IsPregnant 
                        && GetSaturation() < PortionsEatenForMultiply 
                        && TotalDaysCooldownUntil <= entity.World.Calendar.TotalDays
                    )
                ;
            }
        }

        public EntityBehaviorMultiply(Entity entity) : base(entity)
        {

        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            this.typeAttributes = attributes;

            if (entity.World.Side == EnumAppSide.Server)
            {
                if (!multiplyTree.HasAttribute("totalDaysLastBirth"))
                {
                    TotalDaysLastBirth = -9999;
                }

                callbackId = entity.World.RegisterCallback(CheckMultiply, 3000);
            }
        }


        protected virtual void CheckMultiply(float dt)
        {
            if (!entity.Alive) return;

            callbackId = entity.World.RegisterCallback(CheckMultiply, 3000);

            if (entity.World.Calendar == null) return;

            double daysNow = entity.World.Calendar.TotalDays;

            if (!IsPregnant)
            {
                if (TryGetPregnant())
                {
                    IsPregnant = true;
                    TotalDaysPregnancyStart = daysNow;
                }

                return;
            }


            /*if (GrowthCapQuantity > 0 && IsGrowthCapped())
            {
                TimeLastMultiply = entity.World.Calendar.TotalHours;
                return;
            }*/

            
            if (daysNow - TotalDaysPregnancyStart > PregnancyDays)
            {
                Random rand = entity.World.Rand;

                float q = SpawnQuantityMin + (float)rand.NextDouble() * (SpawnQuantityMax - SpawnQuantityMin);
                TotalDaysLastBirth = daysNow;
                TotalDaysCooldownUntil = daysNow + (MultiplyCooldownDaysMin + rand.NextDouble() * (MultiplyCooldownDaysMax - MultiplyCooldownDaysMin));
                IsPregnant = false;
                entity.WatchedAttributes.MarkPathDirty("multiply");

                GiveBirth(q);
            }

            entity.World.FrameProfiler.Mark("multiply");
        }

        protected virtual void GiveBirth(float q)
        {
            Random rand = entity.World.Rand;

            int generation = entity.WatchedAttributes.GetInt("generation", 0);
            if (spawnEntityCodes == null) PopulateSpawnEntityCodes();
            if (spawnEntityCodes != null)
            {
                while (q > 1 || rand.NextDouble() < q)
                {
                    q--;
                    AssetLocation SpawnEntityCode = spawnEntityCodes[rand.Next(spawnEntityCodes.Length)];
                    EntityProperties childType = entity.World.GetEntityType(SpawnEntityCode);
                    if (childType == null) continue;
                    Entity childEntity = entity.World.ClassRegistry.CreateEntity(childType);

                    childEntity.ServerPos.SetFrom(entity.ServerPos);
                    childEntity.ServerPos.Motion.X += (rand.NextDouble() - 0.5f) / 20f;
                    childEntity.ServerPos.Motion.Z += (rand.NextDouble() - 0.5f) / 20f;

                    childEntity.Pos.SetFrom(childEntity.ServerPos);
                    childEntity.Attributes.SetString("origin", "reproduction");
                    childEntity.WatchedAttributes.SetInt("generation", generation + 1);
                    entity.World.SpawnEntity(childEntity);
                }
            }
        }

        protected virtual void PopulateSpawnEntityCodes()
        {
            JsonObject sec = typeAttributes["spawnEntityCodes"];   // Optional fancier syntax in version 1.19+
            if (!sec.Exists)
            {
                sec = typeAttributes["spawnEntityCode"];    // The simple property as it was pre-1.19 - can still be used, suitable for the majority of cases
                if (sec.Exists) spawnEntityCodes = new AssetLocation[] { new AssetLocation(sec.AsString("")) };
                return;
            }
            if (sec.IsArray())
            {
                SpawnEntityProperties[] codes = sec.AsArray<SpawnEntityProperties>();
                spawnEntityCodes = new AssetLocation[codes.Length];
                for (int i = 0; i < codes.Length; i++) spawnEntityCodes[i] = new AssetLocation(codes[i].Code ?? "");
            }
            else
            {
                spawnEntityCodes = new AssetLocation[] { new AssetLocation(sec.AsString("")) };
            }
        }

        public override void TestCommand(object arg)
        {
            GiveBirth((int) arg);
        }

        protected virtual bool TryGetPregnant()
        {
            if (entity.World.Rand.NextDouble() > 0.06) return false;
            if (TotalDaysCooldownUntil > entity.World.Calendar.TotalDays) return false;

            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (tree == null) return false;

            float saturation = tree.GetFloat("saturation", 0);
            
            if (saturation >= PortionsEatenForMultiply)
            {
                Entity maleentity = null;
                if (RequiresNearbyEntityCode != null && (maleentity = GetRequiredEntityNearby()) == null) return false;

                if (entity.World.Rand.NextDouble() < 0.2)
                {
                    tree.SetFloat("saturation", saturation - 1);
                    return false;
                }

                tree.SetFloat("saturation", saturation - PortionsEatenForMultiply);

                if (maleentity != null)
                {
                    ITreeAttribute maletree = maleentity.WatchedAttributes.GetTreeAttribute("hunger");
                    if (maletree != null)
                    {
                        saturation = maletree.GetFloat("saturation", 0);
                        maletree.SetFloat("saturation", Math.Max(0, saturation - 1));
                    }
                }

                IsPregnant = true;
                TotalDaysPregnancyStart = entity.World.Calendar.TotalDays;
                entity.WatchedAttributes.MarkPathDirty("multiply");

                return true;
            }

            return false;
        }

        protected virtual Entity GetRequiredEntityNearby()
        {
            if (RequiresNearbyEntityCode == null) return null;

            return entity.World.GetNearestEntity(entity.ServerPos.XYZ, RequiresNearbyEntityRange, RequiresNearbyEntityRange, (e) =>
            {
                if (e.WildCardMatch(new AssetLocation(RequiresNearbyEntityCode)))
                {
                    if (!e.WatchedAttributes.GetBool("doesEat") || (e.WatchedAttributes["hunger"] as ITreeAttribute)?.GetFloat("saturation") >= 1)
                    {
                        return true;
                    }
                }

                return false;

            });
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            entity.World.UnregisterCallback(callbackId);
        }



        public override string PropertyName()
        {
            return "multiply";
        }

        public override void GetInfoText(StringBuilder infotext)
        {
            multiplyTree = entity.WatchedAttributes.GetTreeAttribute("multiply");

            if (IsPregnant) infotext.AppendLine(Lang.Get("Is pregnant"));
            else
            {
                if (entity.Alive)
                {
                    ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
                    if (tree != null)
                    {
                        float saturation = tree.GetFloat("saturation", 0);
                        infotext.AppendLine(Lang.Get("Portions eaten: {0}", saturation));
                    }

                    double daysLeft = TotalDaysCooldownUntil - entity.World.Calendar.TotalDays;

                    if (daysLeft > 0)
                    {
                        if (daysLeft > 3)
                        {
                            infotext.AppendLine(Lang.Get("Several days left before ready to mate"));
                        }
                        else
                        {
                            infotext.AppendLine(Lang.Get("Less than 3 days before ready to mate"));
                        }

                    }
                    else
                    {
                        infotext.AppendLine(Lang.Get("Ready to mate"));
                    }
                }
            }
        }
    }

    public class SpawnEntityProperties
    {
        [JsonProperty]
        public string Code;
    }
}
