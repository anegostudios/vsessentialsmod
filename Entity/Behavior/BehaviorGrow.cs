﻿using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

#nullable disable

namespace Vintagestory.GameContent
{

    public class EntityBehaviorGrow : EntityBehavior
    {
        ITreeAttribute growTree;
        JsonObject typeAttributes;
        long callbackId = 0;

        public float HoursToGrow { get; set; }
        public float OrPortionsEatenForGrowing { get; set; }

        internal AssetLocation[] AdultEntityCodes
        {
            get { return AssetLocation.toLocations(typeAttributes["adultEntityCodes"].AsArray<string>(System.Array.Empty<string>())); }
        }
        internal AssetLocation[] FedAdultEntityCodes
        {
            get { return AssetLocation.toLocations(typeAttributes["fedAdultEntityCodes"].AsArray<string>(System.Array.Empty<string>())); }
        }

        internal double TimeSpawned
        {
            get { return growTree.GetDouble("timeSpawned"); }
            set { growTree.SetDouble("timeSpawned", value); }
        }



        public EntityBehaviorGrow(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            base.Initialize(properties, typeAttributes);

            this.typeAttributes = typeAttributes;
            HoursToGrow = typeAttributes["hoursToGrow"].AsFloat(96);
            OrPortionsEatenForGrowing = typeAttributes["orPortionsEatenForGrowing"].AsFloat(12);

            growTree = entity.WatchedAttributes.GetTreeAttribute("grow");

            if (growTree == null)
            {
                entity.WatchedAttributes.SetAttribute("grow", growTree = new TreeAttribute());
                TimeSpawned = entity.World.Calendar.TotalHours;
            }

            // Animals released from a cage/trap will stop growing for a year. 
            // Needed for elk taming, also a nice way to keep juvenil animals around - just catch and release em from time to time
            var cal = entity.World.Calendar;
            var totalDaysReleased = entity.Attributes.GetDouble("totalDaysReleased", -cal.DaysPerYear);
            TimeSpawned = Math.Max(TimeSpawned, (totalDaysReleased + cal.DaysPerYear) * cal.HoursPerDay);


            callbackId = entity.World.RegisterCallback(CheckGrowth, 3000);
        }


        private void CheckGrowth(float dt)
        {
            callbackId = 0;
            if (!entity.Alive) return;

            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            bool wasFedToAdulthood = (tree != null && tree.GetFloat("saturation") >= OrPortionsEatenForGrowing);
                
            if (entity.World.Calendar.TotalHours >= TimeSpawned + HoursToGrow || wasFedToAdulthood)
            {
                AssetLocation[] entityCodes = wasFedToAdulthood ? FedAdultEntityCodes : AdultEntityCodes;
                if (entityCodes.Length == 0) return;
                AssetLocation code = entityCodes[entity.World.Rand.Next(entityCodes.Length)];

                EntityProperties adultType = entity.World.GetEntityType(code);

                if (adultType == null)
                {
                    entity.World.Logger.Error("Misconfigured entity. Entity with code '{0}' is configured (via Grow behavior) to grow into '{1}', but no such entity type was registered.", entity.Code, code);
                    return;
                }

                Cuboidf collisionBox = adultType.SpawnCollisionBox;

                // Delay adult spawning if we're colliding
                if (entity.World.CollisionTester.IsColliding(entity.World.BlockAccessor, collisionBox, entity.ServerPos.XYZ, false))
                {
                    callbackId = entity.World.RegisterCallback(CheckGrowth, 3000);
                    return;
                }

                Entity adult = entity.World.ClassRegistry.CreateEntity(adultType);
                adult.ServerPos.SetFrom(entity.ServerPos);
                adult.Pos.SetFrom(adult.ServerPos);
                bool keepTextureIndex = entity.Properties.Client != null 
                    && entity.Properties.Client.TexturesAlternatesCount > 0 
                    && adultType.Client != null 
                    && entity.Properties.Client.TexturesAlternatesCount == adultType.Client.TexturesAlternatesCount;

                adult.Attributes.SetBool("wasFedToAdulthood", wasFedToAdulthood);
                BecomeAdult(adult, keepTextureIndex);
            } else
            {
                callbackId = entity.World.RegisterCallback(CheckGrowth, 3000);
                double age = entity.World.Calendar.TotalHours - TimeSpawned;
                if (age >=  0.1 * HoursToGrow)
                {
                    float newAge = (float)(age / HoursToGrow - 0.1);
                    if (newAge >= 1.01f * growTree.GetFloat("age"))
                    {
                        growTree.SetFloat("age", newAge);
                        entity.WatchedAttributes.MarkPathDirty("grow");
                    }
                }
            }

            entity.World.FrameProfiler.Mark("checkgrowth");
        }

        protected virtual void BecomeAdult(Entity adult, bool keepTextureIndex)
        {
            // Set adult attribute before we spawn it, so that it initialises correctly (and before the child entity dies!)
            adult.WatchedAttributes.SetInt("generation", entity.WatchedAttributes.GetInt("generation", 0));
            adult.WatchedAttributes.SetDouble("birthTotalDays", entity.World.Calendar.TotalDays);
            // Transfer the textureIndex of the child to the adult, if both have same number of alternates (e.g. used for pullets)
            if (keepTextureIndex && entity.WatchedAttributes.HasAttribute("textureIndex"))
            {
                adult.WatchedAttributes.SetAttribute("textureIndex", entity.WatchedAttributes.GetAttribute("textureIndex"));
            }

            entity.Die(EnumDespawnReason.Expire, null);
            entity.World.SpawnEntity(adult);
        }


        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            entity.World.UnregisterCallback(callbackId);
        }


        public override string PropertyName()
        {
            return "grow";
        }
    }
}
