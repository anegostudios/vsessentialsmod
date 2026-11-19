using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    /// <summary>
    /// Allows an entity to get pregnant and give birth.
    /// <br/>Uses the "multiply" code
    /// </summary>
    /// <example><code lang="json">
    ///"behaviors": [
    /// {
    ///     "code": "multiply",
    ///     "enabledByType": {
    ///         "*-female": true,
    ///         "*": false
    ///     },
    ///     "spawnEntityCodes": [{ "code": "sheep-{type}-baby-male" }, { "code": "sheep-{type}-baby-female" }],
    ///     "requiresNearbyEntityCode": "sheep-bighorn-adult-male",
    ///     "requiresNearbyEntityRange": 10,
    ///     "spawnQuantityMin": 1,
    ///     "spawnQuantityMax": 1,
    ///     "pregnancyDays": 20,
    ///     "multiplyCooldownDaysMin": 4,
    ///     "multiplyCooldownDaysMax": 11,
    ///     "portionsEatenForMultiply": 10
    /// },
    ///],
    /// </code></example>
    [DocumentAsJson]
    [AddDocumentationProperty("spawnEntityCode", "Used instead of spawnEntityCodes if the latter is not defined", "System.String", "Optional", "", false)]
    public class EntityBehaviorMultiply : EntityBehaviorMultiplyBase
    {
        protected long callbackId = 0;


        /// <summary>
        /// The entity codes used to spawn the offspring
        /// </summary>
        [DocumentAsJson("Required", "")]
        protected AssetLocation[]? SpawnEntityCodes;

        /// <summary>
        /// Specifies an list of entities required nearby for this entity to be able to get pregnant. Only one needs to match.
        /// </summary>
        [DocumentAsJson("Optional", "")]
        [JsonProperty] public AssetLocation[]? RequiresNearbyEntityCodes;

        /// <summary>
        /// Specifies an entity required for this entity to be able to get pregnant. Will only set if plural version is not set.
        /// </summary>
        [DocumentAsJson("Optional", "")]
        [JsonProperty] private AssetLocation requiresNearbyEntityCode { set => RequiresNearbyEntityCodes ??= [ value ]; }


        /// <summary>
        /// How long the pregnancy should last in in-game days.
        /// </summary>
        [DocumentAsJson("Optional", "3")]
        [JsonProperty] public double PregnancyDays = 3;

        /// <summary>
        /// Pregnancy length will vary by plus or minus this fraction of the total length
        /// </summary>
        [DocumentAsJson("Optional", "0.04")]
        [JsonProperty] protected double PregnancyLengthVariation = 0.04;

        /// <summary>
        /// Specifies the range within which the entity with code defined in <see cref="RequiresNearbyEntityCode"/> should be located in order for this entity to be able to get pregnant
        /// </summary>
        [DocumentAsJson("Optional", "5")]
        [JsonProperty] public float RequiresNearbyEntityRange = 5;

        /// <summary>
        /// How many offspring should be spawned at minimum
        /// </summary>
        [DocumentAsJson("Optional", "1")]
        [JsonProperty] public float SpawnQuantityMin = 1;

        /// <summary>
        /// How many offspring should be spawned at maximum
        /// </summary>
        [DocumentAsJson("Optional", "2")]
        [JsonProperty] public float SpawnQuantityMax = 2;

        /// <summary>
        /// For each offspring between the min and the max, this is the chance of spawning. This makes the litter size a bell curve instead of a uniform.
        /// </summary>
        [DocumentAsJson("Optional", "0.5")]
        [JsonProperty] public float SpawnQuantityChance = 0.5f;


        /// <summary>
        /// At what density do they stop breeding
        /// </summary>
        [DocumentAsJson("Optional", "8")]
        [JsonProperty] public float SquareMetersPerEntity { get; set; } = 8;

        /// <summary>
        /// Check these distances to see if there are too many entities nearby. All these checks must pass or the conditions are considered crowded.
        /// For example, [4, 8] will check for entities 4 blocks away and 8 blocks away, taxicab distance. The first check fails if there are more than (4 * 2)^2 / SquareMetersPerEntity animals nearby.
        /// </summary>
        [DocumentAsJson("Optional", "[4, 8]")]
        [JsonProperty] public float[] CrowdingDistanceChecks { get; set; } = [4, 8];

        /// <summary>
        /// Entities with any of these tags count towards the density.
        /// </summary>
        [DocumentAsJson("Optional", "[\"animal\"]")]
        public TagSetFast CrowdingTags;


        /// <summary>
        /// Wait this long (as a fraction of the total pregnancy length) before discarding non-viable embryos
        /// </summary>
        [DocumentAsJson("Optional", "0.125")]
        [JsonProperty] protected double ViabilityCheckDelay = 0.125;
        /// <summary>
        /// Fraction of embryos considered non-viable, randomly determined (a second check after gene viability)
        /// </summary>
        [DocumentAsJson("Optional", "0")]
        [JsonProperty] protected double MiscarriageChance = 0;
        /// <summary>
        /// After a miscarriage (either random or genetic), wait this long as a fraction of the normal multiply cooldown before being ready to mate again
        /// </summary>
        [DocumentAsJson("Optional", "0.5")]
        [JsonProperty] protected double MiscarriageCooldown = 0.5;


        public string? CooldownReason
        {
            get { return multiplyTree.GetString("cooldownReason"); }
            set
            {
                if (value == null) multiplyTree.RemoveAttribute("cooldownReason");
                else multiplyTree.SetString("cooldownReason", value);
                entity.WatchedAttributes.MarkPathDirty("multiply"); 
            }
        }

        public double TotalDaysLastBirth
        {
            get { return multiplyTree.GetDouble("totalDaysLastBirth", -9999); }
            set { multiplyTree.SetDouble("totalDaysLastBirth", value); entity.WatchedAttributes.MarkPathDirty("multiply"); }
        }

        public double TotalDaysPregnancyStart
        {
            get { return multiplyTree.GetDouble("totalDaysPregnancyStart"); }
            set { multiplyTree.SetDouble("totalDaysPregnancyStart", value); entity.WatchedAttributes.MarkPathDirty("multiply"); }
        }

        [MemberNotNullWhen(true, "Litter")]
        public bool IsPregnant => Litter != null;

        protected TreeArrayAttribute? Litter
        {
            get => multiplyTree["litter"] as TreeArrayAttribute;
            set { 
                if (value == null) multiplyTree.RemoveAttribute("litter");
                else multiplyTree["litter"] = value;
                entity.WatchedAttributes.MarkPathDirty("multiply");
            }
        }

        protected double TotalDaysPregnancyEnd
        {
            get => multiplyTree.GetDouble("totalDaysPregnancyEnd");
            set {
                multiplyTree.SetDouble("totalDaysPregnancyEnd", value);
                entity.WatchedAttributes.MarkPathDirty("multiply");
            }
        }

        public bool InEarlyPregnancy
        {
            get => multiplyTree.GetBool("earlyPregnancy", true);
            set {
                multiplyTree.SetBool("earlyPregnancy", value);
                entity.WatchedAttributes.MarkPathDirty("multiply");
            }
        }

        public void SetNotPregnant()
        {
            multiplyTree.RemoveAttribute("litter");
            entity.WatchedAttributes.MarkPathDirty("multiply");
        }

        public override bool ShouldEat => eatAnyway ||
            (
                !IsPregnant
                && GetSaturation() < PortionsEatenForMultiply
                && TotalDaysCooldownUntil <= entity.World.Calendar.TotalDays
            );

        public EntityBehaviorMultiply(Entity entity) : base(entity)
        {

        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            // Populate spawn entity codes
            JsonObject sec = attributes["spawnEntityCodes"];   // Optional fancier syntax in version 1.19+
            SpawnEntityProperties[]? codes = sec.AsNonNullArray<SpawnEntityProperties>("spawnEntityCodes for multiply behavior of entity " + entity.Code);
            if (codes != null)
            {
                SpawnEntityCodes = Array.ConvertAll(codes, c => AssetLocation.Create(c?.Code ?? "", entity.Code.Domain));
            }
            else
            {
                if (!sec.Exists)
                {
                    sec = attributes["spawnEntityCode"];    // The simple property as it was pre-1.19
                }
                SpawnEntityCodes = [ AssetLocation.Create(sec.AsString(""), entity.Code.Domain) ];
            }


            JsonObject tagsJson = attributes["crowdingTags"];
            string[] tags = tagsJson.AsNonNullArray<string>("tags for multiply behavior of entity " + entity.Code) ?? ["animal"];
            this.CrowdingTags = entity.Api.EntityTagRegistry.CreateTagSet(tags);

            if (entity.World.Side == EnumAppSide.Server)
            {
                if (!multiplyTree.HasAttribute("totalDaysLastBirth"))
                {
                    TotalDaysLastBirth = -9999;
                }

                callbackId = entity.World.RegisterCallback(CheckMultiply, 3000);
            }
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            if (entity.World.Side == EnumAppSide.Server)
            {
                // Backwards compat - keep existing pregnancies on saves from 1.22 and earlier
                if (multiplyTree.GetBool("isPregnant"))
                {
                    multiplyTree.RemoveAttribute("isPregnant");
                    double totalDaysPregnancyStart = TotalDaysPregnancyStart;

                    EntityBehaviorGenetics? geneticsBehavior = entity.GetBehavior<EntityBehaviorGenetics>();
                    Genome? sireGenome = null;
                    if (geneticsBehavior != null)
                    {
                        // Depending on behavior list order this could happen. Genetics is ok to after-initialize twice.
                        if (geneticsBehavior.Genome == null)
                        {
                            geneticsBehavior.AfterInitialized(onFirstSpawn);
                        }

                        GeneInitializer? initializer = geneticsBehavior.GenomeType.DefaultInitializer;
                        sireGenome = new Genome(geneticsBehavior.GenomeType, initializer, true, entity.World.Rand);
                    }
                    ConceiveLitter(sireGenome, null);

                    if (IsPregnant)
                    {
                        TotalDaysPregnancyStart = totalDaysPregnancyStart;
                        double rate = 1 + PregnancyLengthVariation * (entity.World.Rand.NextDouble() - 0.5) * 2;
                        TotalDaysPregnancyEnd = totalDaysPregnancyStart + rate * PregnancyDays;
                    }
                }
            }
        }

        public virtual int ChooseLitterSize()
        {
            int litterSize = (int)SpawnQuantityMin;
            for (int i = 0; i < (SpawnQuantityMax - SpawnQuantityMin); ++i)
            {
                if (entity.World.Rand.NextDouble() < SpawnQuantityChance)
                {
                    litterSize += 1;
                }
            }
            return litterSize;
        }

        protected virtual void CheckMultiply(float dt)
        {
            if (!entity.Alive)
            {
                callbackId = 0;
                return;
            }

            callbackId = entity.World.RegisterCallback(CheckMultiply, 3000);

            if (entity.World.Calendar == null) return;

            double daysNow = entity.World.Calendar.TotalDays;

            if (!IsPregnant)
            {
                if (entity.World.Rand.NextDouble() < 0.06)
                {
                    TryGetPregnant();
                }
                return;
            }

            if (InEarlyPregnancy)
            {
                if (entity.World.Calendar.TotalDays > TotalDaysPregnancyStart + PregnancyDays * ViabilityCheckDelay)
                {
                    EntityBehaviorGenetics? gb = entity.GetBehavior<EntityBehaviorGenetics>();

                    bool anyLethal = false;
                    List<TreeAttribute> surviving = new List<TreeAttribute>();
                    foreach (TreeAttribute childTree in Litter.value)
                    {
                        if (childTree.GetBool("viable", true) == false) continue;

                        if (gb != null)
                        {
                            ITreeAttribute? childGeneticsTree = childTree.GetTreeAttribute("genetics");
                            if (childGeneticsTree != null && gb.IsEmbryonicLethal(new Genome(gb.GenomeType, childGeneticsTree)))
                            {
                                anyLethal = true;
                                continue;
                            }
                        }

                        surviving.Add(childTree);
                    }
                    if (surviving.Count == 0)
                    {
                        SetNotPregnant();
                        double cooldown = MultiplyCooldownDaysMin + entity.World.Rand.NextDouble() * (MultiplyCooldownDaysMax - MultiplyCooldownDaysMin);
                        TotalDaysCooldownUntil = entity.World.Calendar.TotalDays + MiscarriageCooldown * cooldown;
                        if (anyLethal)
                        {
                            multiplyTree.SetDouble("geneticLethalLossTotalHours", entity.World.Calendar.TotalHours);
                        }
                    }
                    else
                    {
                        Litter.value = surviving.ToArray();
                    }
                    entity.WatchedAttributes.MarkPathDirty("multiply");
                    InEarlyPregnancy = false;
                }
                return;
            }
            
            if (daysNow > TotalDaysPregnancyEnd)
            {
                GiveBirth();
            }

            entity.World.FrameProfiler.Mark("multiply");
        }

        public virtual void MateWith(Entity sire)
        {
            ArgumentNullException.ThrowIfNull(sire);
            ITreeAttribute? tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            float saturation = tree?.GetFloat("saturation", 0) ?? 0;
            tree?.SetFloat("saturation", saturation - PortionsEatenForMultiply);
            entity.WatchedAttributes.MarkPathDirty("hunger");

            if (sire != entity)
            {
                ITreeAttribute? maletree = sire.WatchedAttributes.GetTreeAttribute("hunger");
                if (maletree != null)
                {
                    saturation = maletree.GetFloat("saturation", 0);
                    maletree.SetFloat("saturation", Math.Max(0, saturation - 1));
                    sire.WatchedAttributes.MarkPathDirty("hunger");
                }
            }

            Genome? sireGenome = sire.GetBehavior<EntityBehaviorGenetics>()?.Genome;
            ConceiveLitter(sireGenome, sire.LogicalId);
        }

        public virtual void ConceiveLitter(Genome? sireGenome, long? fatherId)
        {
            if (SpawnEntityCodes == null)
            {
                entity.Api.Logger.Warning("Entity " + entity.Code + " multiply behavior expected non-null SpawnEntityCodes");
                return;
            }
            EntityBehaviorGenetics? geneticsBehavior = entity.GetBehavior<EntityBehaviorGenetics>();

            int litterSize = ChooseLitterSize();
            List<TreeAttribute> litter = new List<TreeAttribute>();
            for (int i = 0; i < litterSize; ++i)
            {
                TreeAttribute offspring = new TreeAttribute();
                if (entity.World.Rand.NextSingle() < MiscarriageChance)
                {
                    if (ViabilityCheckDelay <= 0) continue;
                    offspring.SetBool("viable", false); // Predetermined, to avoid save-scumming
                }
                AssetLocation offspringCode = SpawnEntityCodes[entity.World.Rand.Next(SpawnEntityCodes.Length)];
                if (geneticsBehavior != null && sireGenome != null)
                {
                    Genome ourGenome = geneticsBehavior.Genome;
                    EntityProperties? childType = entity.World.GetEntityType(offspringCode);
                    if (childType == null)
                    {
                        entity.Api.Logger.Warning("Couldn't find offspring entity with code " + offspringCode + " defined in multiply behavior of " + entity.Code);
                        continue;
                    }
                    bool heterogametic = ourGenome.Type.SexDetermination.Heterogametic(EntityBehaviorGenetics.EntityIsMale(childType));
                    Genome child = Genome.Inherit(ourGenome, sireGenome, heterogametic, entity.World.Rand);
                    child.Mutate(geneticsBehavior.MutationRate, entity.World.Rand, geneticsBehavior.AllowedMutations);
                    TreeAttribute childGeneticsTree = (TreeAttribute) offspring.GetOrAddTreeAttribute("genetics");
                    if (ViabilityCheckDelay <= 0 && geneticsBehavior.IsEmbryonicLethal(child))
                    {
                        continue;
                    }
                    child.AddToTree(childGeneticsTree);
                }
                offspring.SetString("code", offspringCode.ToString());

                offspring.SetLong("motherId", entity.LogicalId);
                if (fatherId != null) offspring.SetLong("fatherId", fatherId.Value);

                litter.Add(offspring);
            }

            if (litter.Count == 0)
            {
                if (litterSize > 0 && MiscarriageCooldown > 0)
                {
                    double cooldown = MultiplyCooldownDaysMin + entity.World.Rand.NextDouble() * (MultiplyCooldownDaysMax - MultiplyCooldownDaysMin);
                    TotalDaysCooldownUntil = entity.World.Calendar.TotalDays + MiscarriageCooldown * cooldown;
                }
                return;
            }

            InEarlyPregnancy = ViabilityCheckDelay > 0;

            TreeArrayAttribute litterData = new TreeArrayAttribute();
            litterData.value = litter.ToArray();
            Litter = litterData;

            TotalDaysPregnancyStart = entity.World.Calendar.TotalDays;
            double rate = 1 + PregnancyLengthVariation * (entity.World.Rand.NextDouble() - 0.5) * 2;
            TotalDaysPregnancyEnd = TotalDaysPregnancyStart + rate * PregnancyDays;
        }

        protected virtual void GiveBirth()
        {
            int nextGeneration = entity.WatchedAttributes.GetInt("generation", 0) + 1;
            TreeAttribute[]? litterData = Litter?.value;

            if (litterData == null)
            {
                entity.Api.Logger.Warning("Entity " + entity.Code + " gave birth but with null litter data, so 0 offspring were produced.");
            }
            else {
                foreach (TreeAttribute childData in litterData)
                {
                    Entity? spawn = SpawnNewborn(entity.World, entity.Pos, entity, nextGeneration, childData);
                }
            }
            SetNotPregnant();
            TotalDaysLastBirth = entity.World.Calendar.TotalDays;
            TotalDaysCooldownUntil = entity.World.Calendar.TotalDays + MultiplyCooldownDaysMin + entity.World.Rand.NextDouble() * (MultiplyCooldownDaysMax - MultiplyCooldownDaysMin);
        }

        public static Entity? SpawnNewborn(IWorldAccessor world, EntityPos pos, Entity? foster, int nextGeneration, TreeAttribute childData)
        {
            if (world.Side != EnumAppSide.Server) return null;

            AssetLocation spawnCode = new AssetLocation(childData.GetString("code"));
            if (((IServerWorldAccessor)world).RemappedEntities.TryGetValue(spawnCode, out string? remap))
            {
                spawnCode = remap;
            }
            EntityProperties? spawnType = world.GetEntityType(spawnCode);
            if (spawnType == null)
            {
                world.Logger.Warning(foster?.Code.ToString() + " attempted to hatch or give birth to entity with code " 
                    + spawnCode.ToString() + ", but no such entity was found.");
                return null;
            }
            Entity spawn = world.ClassRegistry.CreateEntity(spawnType);
            spawn.Pos.SetFrom(pos);
            spawn.Pos.Yaw = world.Rand.NextSingle() * GameMath.TWOPI;
            Random random = world.Rand;
            spawn.Pos.Motion.X += (random.NextDouble() - 0.5f) / 20f;
            spawn.Pos.Motion.Z += (random.NextDouble() - 0.5f) / 20f;
            spawn.Attributes.SetString("origin", "reproduction");
            spawn.WatchedAttributes.SetInt("generation", nextGeneration);

            childData.RemoveAttribute("code");
            spawn.WatchedAttributes.MergeTree(childData);

            if (foster != null)
            {
                spawn.WatchedAttributes.SetLong("fosterId", foster.LogicalId);
            }
            spawn.WatchedAttributes.SetDouble("birthTotalDays", world.Calendar.TotalDays);

            world.SpawnEntity(spawn);
            return spawn;
        }

        public override void TestCommand(object arg)
        {
            ConceiveLitter(null, null);
            GiveBirth();
        }

        protected virtual bool TryGetPregnant()
        {
            if (TotalDaysCooldownUntil > entity.World.Calendar.TotalDays) return false;

            CooldownReason = null;
            ITreeAttribute? tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
            if (tree == null) return false;

            foreach (float dist in CrowdingDistanceChecks)
            {
                int nearby = entity.World.GetEntitiesAround(entity.Pos.XYZ, dist, dist / 2, (e) => CrowdingTags.Overlaps(e.Tags)).Length;
                if (nearby > dist * dist * 4 / SquareMetersPerEntity)
                {
                    TotalDaysCooldownUntil = entity.World.Calendar.TotalDays + 0.125f;
                    CooldownReason = "entity-toocrowded";
                    return false;
                }
            }

            float saturation = tree.GetFloat("saturation", 0);
            if (saturation >= PortionsEatenForMultiply)
            {
                Entity? sire = GetRequiredEntityNearby();
                if (sire == null) return false;

                // This is somewhat less than ideal: Female entities sometimes consume food, but only while not pregnant AND near a male
                if (entity.World.Rand.NextDouble() < 0.2)
                {
                    tree.SetFloat("saturation", saturation - 1);
                    entity.WatchedAttributes.MarkPathDirty("hunger");
                    return false;
                }

                MateWith(sire);
                return true;
            }

            return false;
        }

        protected virtual Entity? GetRequiredEntityNearby()
        {
            if (RequiresNearbyEntityCodes == null) return entity;
            if (RequiresNearbyEntityCodes.Length == 0) return null;

            Entity[] entities = entity.World.GetEntitiesAround(entity.Pos.XYZ, RequiresNearbyEntityRange, RequiresNearbyEntityRange / 2,
                (e) => {
                    foreach (AssetLocation sire in RequiresNearbyEntityCodes)
                    {
                        if (e.WildCardMatch(sire))
                        {
                            return EntityCanMate(e);
                        }
                    }
                    return false;
                }
            );
            if (entities == null || entities.Length == 0) return null;

            if (entity.World.Rand.NextSingle() < 0.1f)
            {
                return entities[entity.World.Rand.Next(entities.Length)];
            }
            return ChooseAvoidingCloseRelatives(entity, entities);
        }

        public static Entity? ChooseAvoidingCloseRelatives(Entity entity, Entity[] entities)
        {
            Entity best = entities[0];
            bool closeRelative = IsCloseRelative(entity, best);
            float distance = entity.Pos.SquareDistanceTo(best.Pos);
            for (int i = 1; i < entities.Length; ++i)
            {
                float currentDistance = entity.Pos.SquareDistanceTo(entities[i].Pos);
                if (closeRelative && !IsCloseRelative(entity, entities[i]))
                {
                    best = entities[i];
                    closeRelative = false;
                    distance = currentDistance;
                    continue;
                }
                if (!closeRelative && IsCloseRelative(entity, entities[i]))
                {
                    continue;
                }
                if (distance > currentDistance)
                {
                    best = entities[i];
                    distance = currentDistance;
                }
            }
            return best;
        }

        public static bool IsCloseRelative(Entity entity, Entity other)
        {
            long ourID = entity.LogicalId;
            long theirID = other.LogicalId;
            long ourMother = entity.WatchedAttributes.GetLong("motherId", -1);
            long ourFather = entity.WatchedAttributes.GetLong("fatherId", -1);
            long ourFoster = entity.WatchedAttributes.GetLong("fosterId", -1);
            long theirMother = other.WatchedAttributes.GetLong("motherId", -1);
            long theirFather = other.WatchedAttributes.GetLong("fatherId", -1);
            long theirFoster = other.WatchedAttributes.GetLong("fosterId", -1);

            bool isParent = ourID == theirMother || ourID == theirFather || ourID == theirFoster;
            bool isChild = theirID == ourMother || theirID == ourFather || theirID == ourFoster;
            // Skip sibling check for adoption
            bool sharesMother = ourMother != -1 && (ourMother == theirMother || ourMother == theirFather);
            bool sharesFather = ourFather != -1 && (ourFather == theirFather || ourFather == theirMother);
            return isParent || isChild || sharesMother || sharesFather;
        }

        protected virtual bool EntityCanMate(Entity e)
        {
            return e.Alive && (!e.WatchedAttributes.GetBool("doesEat") || (e.WatchedAttributes["hunger"] as ITreeAttribute)?.GetFloat("saturation") >= 1);
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
            if (multiplyTree == null) return; // Can happen in the first few frames

            if (IsPregnant)
            {
                if (InEarlyPregnancy) 
                {
                    infotext.AppendLine(Lang.Get("multiply-earlypregnancy"));
                }
                else
                {
                    int passed = (int)Math.Round(entity.World.Calendar.TotalDays - TotalDaysPregnancyStart);
                    infotext.AppendLine(Lang.Get("multiply-pregnancy", passed));
                }
                return;
            }
            else if (entity.World.Calendar.TotalHours - multiplyTree.GetDouble("geneticLethalLossTotalHours", -999999) < entity.World.Calendar.HoursPerDay * 2)
            {
                infotext.AppendLine(Lang.Get("multiply-miscarriage-lethalgenes"));
            }
            else
            {
                if (entity.Alive)
                {
                    ITreeAttribute? tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
                    if (tree != null)
                    {
                        float saturation = tree.GetFloat("saturation", 0);
                        infotext.AppendLine(Lang.Get("Portions eaten: {0}", saturation));
                    }

                    double daysLeft = TotalDaysCooldownUntil - entity.World.Calendar.TotalDays;

                    if (daysLeft > 0)
                    {
                        string? cooldownReason = CooldownReason;
                        if (cooldownReason != null)
                        {
                            infotext.AppendLine(Lang.Get(cooldownReason));
                        }
                        else if (daysLeft > 5)
                        {
                            infotext.AppendLine(Lang.Get("multiply-notready-severaldays"));
                        }
                        else
                        {
                            infotext.AppendLine(Lang.Get("multiply-daysuntilready", (int)Math.Ceiling(daysLeft)));
                        }

                    }
                    else
                    {
                        infotext.AppendLine(Lang.Get("multiply-ready"));
                    }
                }
            }
        }
    }

    public class SpawnEntityProperties
    {
        [JsonProperty]
        public required string Code;
    }
}
