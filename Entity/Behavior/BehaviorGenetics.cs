using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Vintagestory.GameContent
{
    public class EntityBehaviorGenetics : EntityBehavior
    {
        public const string Code = "genetics";

        public virtual float MutationRate { get; set; } = 0.0001f; 

        public GenomeType GenomeType { get; set; } = null!;
        private Genome genome = null!;
        public Genome Genome
        {
            get => genome;
            set
            {
                genome = value;
                GenomeModified();
            }
        }
        /// <summary> When an animal spawns, it chooses its initial genes based on the chances provided from the initializer. This lists allowed initializers.
        /// If left null, any initializers on the genome type can be used. Not set on the client. </summary>
        protected string[]? initializers;
        /// <summary> In case none of the initializers are valid for the climate, fall back to this one. Not set on the client. </summary>
        protected GeneInitializer? defaultInitializer;
        /// <summary> Restricts mutations to only change to something with a non-zero chance in the initializer named here. If left null, all mutations are allowed. Not set on the client. </summary>
        public GeneInitializer? AllowedMutations;
        /// <summary> For client-side or entity-specific gene interpreters, that is, the things that turn gene data into traits. AKA genotype into phenotype. </summary>
        protected IGeneInterpreter[]? extraInterpreters;

        // Keys are materials, values are texture baked names. Only includes those textures which should be freed from the atlas when no longer used.
        protected FastSmallDictionary<string, AssetLocation>? AllocatedTextureBakedNames;

        public IEnumerable<IGeneInterpreter> AllInterpreters => Genome.Type.Interpreters?.Concat(extraInterpreters ?? []) ?? extraInterpreters ?? [];

        public EntityBehaviorGenetics(Entity entity) : base(entity) { }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            var modsystem = entity.Api.ModLoader.GetModSystem<ModSystemGenetics>() ?? throw new Exception("Expected a ModSystemGenetics to be available");
            GenomeType = modsystem.GetGenomeType(
                AssetLocation.Create(attributes["genomeType"].AsString(), entity.Code.Domain)
            );

            MutationRate = attributes["mutationRate"].AsFloat(MutationRate);

            if (entity.Api.Side == EnumAppSide.Server)
            {
                string? definit = attributes["defaultInitializer"].AsString();
                defaultInitializer = definit == null ? GenomeType.DefaultInitializer : GenomeType.GetInitializer(definit);

                string? mutations = attributes["allowedMutations"].AsString();
                if (mutations != null)
                {
                    AllowedMutations = GenomeType.GetInitializer(mutations);
                }

                initializers = attributes["initializers"].AsNonNullArray<string>("genetics behavior of " + entity.Code);
            }

            JsonObject[]? jsonInterpreters = attributes["extraInterpreters"].AsArray();
            if (jsonInterpreters != null)
            {
                try
                {
                    extraInterpreters = new IGeneInterpreter[jsonInterpreters.Length];
                    for (int i = 0; i < jsonInterpreters.Length; ++i)
                    {
                        extraInterpreters[i] = modsystem.GetInterpreter(entity.Api, GenomeType, jsonInterpreters[i], entity.Code.Domain, "genetics behavior of entity " + entity.Code);
                    }
                }
                catch (Exception e)
                {
                    entity.Api.Logger.Warning("Error while setting up extraInterpreters on genetics behavior of " + entity.Code);
                    entity.Api.Logger.Error(e);
                    extraInterpreters = null;
                }
            }

            ITreeAttribute? geneticsTree = entity.WatchedAttributes.GetTreeAttribute("genetics");
            if (geneticsTree != null)
            {
                Genome = new Genome(GenomeType, geneticsTree);
            }
            // Else, wait until AfterInitialized and generate a new genome there
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            if (entity.World.Api is ICoreServerAPI sapi)
            {
                if (Genome == null)
                {
                    Random random = entity.World.Rand;
                    bool heterogametic = GenomeType.SexDetermination.Heterogametic(EntityIsMale(entity.Properties));
                    BlockPos blockPos = entity.Pos.AsBlockPos;
                    ClimateCondition climate = entity.Api.World.BlockAccessor.GetClimateAt(blockPos);

                    GeneInitializer? initializer = null;
                    string? initializerOverride = entity.WatchedAttributes.GetTreeAttribute("spawner")?.GetString("geneInitializer");
                    if (initializerOverride != null)
                    {
                        initializer = GenomeType.GetInitializerOrLogWarning(initializerOverride, entity.Api.Logger, "itemstack attribute override on entity type " + entity.Code);
                    }
                    initializer ??= GenomeType.ChooseInitializer(initializers, climate, blockPos.Y, random, sapi) ?? defaultInitializer;

                    Genome = new Genome(GenomeType, initializer, heterogametic, random);
                    Genome.Mutate(MutationRate, random, AllowedMutations);
                    // Is the genome missing because the animal is new, or because the genetics feature on it is new?
                    // For the latter, set up genes to keep the animal looking similar to before.
                    if (!onFirstSpawn)
                    {
                        foreach (IGeneInterpreter interpreter in AllInterpreters)
                        {
                            interpreter.MatchPhenotype(Genome, entity.WatchedAttributes, entity);
                        }
                    }
                    foreach (IGeneInterpreter interpreter in AllInterpreters)
                    {
                        interpreter.FinalizeSpawn(Genome, initializer, random);
                    }
                }
            }
            foreach (IGeneInterpreter interpreter in AllInterpreters)
            {
                interpreter.Interpret(Genome, entity.WatchedAttributes, entity.World.Rand, entity);
            }
        }

        public void GenomeModified()
        {
            if (entity.World.Side == EnumAppSide.Client)
            {
                return;
            }
            TreeAttribute geneticsTree = (TreeAttribute) entity.WatchedAttributes.GetOrAddTreeAttribute("genetics");
            genome.AddToTree(geneticsTree);
            entity.WatchedAttributes.MarkPathDirty("genetics");
        }

        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[]? willDeleteElements)
        {
            ICoreClientAPI? clientAPI = entity.Api as ICoreClientAPI;

            if (clientAPI == null) return;

            List<(AssetLocation, Shape)>? overlays = null;
            foreach (IGeneInterpreter interpreter in AllInterpreters)
            {
                interpreter.GatherShapeOverlays(Genome, entity.WatchedAttributes, ref overlays, shapePathForLogging, ref willDeleteElements);
            }

            if (overlays == null || overlays.Count == 0) return;

            if (!shapeIsCloned)
            {
                entityShape = entityShape.Clone();
                shapeIsCloned = true;
            }

            string? prefix = null;
            foreach ((AssetLocation shapePath, Shape? overlayShape) in overlays)
            {
                Shape? partShape = overlayShape ?? Shape.TryGet(entity.Api, shapePath);

                if (partShape == null)
                {
                    entity.Api.Logger.Warning("Entity shape part {0} defined in genetics for {1} not found or errored, was supposed to be at {2}. Shape part will be invisible.", shapePath, entity.Properties.Code, shapePath);
                    continue;
                }

                partShape.SubclassForStepParenting(prefix);

                var textures = entity.Properties.Client.Textures;
                entityShape.StepParentShape(partShape, shapePath.ToShortString(), shapePathForLogging, entity.Api.Logger, (texcode, loc) =>
                {
                    if (prefix == null && textures.ContainsKey(texcode)) return;

                    var cmpt = textures[prefix == null ? texcode : prefix + "-" + texcode] = new CompositeTexture(loc);
                    cmpt.Bake(entity.Api.Assets);
                    clientAPI.EntityTextureAtlas.GetOrInsertTexture(cmpt.Baked.TextureFilenames[0], out int textureSubid, out _);
                    cmpt.Baked.TextureSubId = textureSubid;
                });
            }
        }

        public override ITexPositionSource? GetTextureSource(ref EnumHandling handling)
        {
            ITexPositionSource? source = null;
            foreach (IGeneInterpreter interpreter in AllInterpreters)
            {
                source = interpreter.GetTextureSource(Genome, entity.WatchedAttributes, ref handling);
                if (handling == EnumHandling.PreventSubsequent)
                {
                    return source;
                }
            }

            if (handling == EnumHandling.PreventDefault) return source;

            FastSmallDictionary<string, int> mapping = new(entity.Properties.Client.Textures.Count);
            bool anyOverlay = false;

            var capi = (ICoreClientAPI)entity.Api;
            IDictionary<string, CompositeTexture> textures = entity.Properties.Client.Textures;
            foreach (string material in textures.Keys)
            {
                bool replaceBaseTexture = false;
                List<BlendedOverlayTexture>? textureOverlays = null;
                foreach (IGeneInterpreter interpreter in AllInterpreters)
                {
                    interpreter.GatherTextureOverlays(Genome, entity.WatchedAttributes, ref textureOverlays, material, ref replaceBaseTexture);
                }
                AssetLocation? prevBakedName = null;
                AllocatedTextureBakedNames?.TryGetValue(material, out prevBakedName);
                AllocatedTextureBakedNames?.Remove(material);

                CompositeTexture texture = textures[material];
                mapping[material] = texture.Baked.TextureSubId;
                int variant = 0;
                if (texture.Alternates != null && texture.Alternates.Count() > 0)
                {
                    variant = entity.WatchedAttributes.GetInt("textureIndex", 0) % (texture.Alternates.Count() + 1);
                    if (entity.WatchedAttributes.HasAttribute(material + "TextureIndex"))
                    {
                        variant = entity.WatchedAttributes.GetInt(material + "TextureIndex", 0) % (texture.Alternates.Count() + 1);
                    }
                    if (variant > 0)
                    {
                        mapping[material] = texture.Baked.BakedVariants[variant].TextureSubId;
                    }
                }

                if (textureOverlays != null && textureOverlays.Count > 0)
                {
                    anyOverlay = true;
                    handling = EnumHandling.PreventSubsequent;

                    // Unlike alternates, baked variants use an index matching the total number of textures
                    AssetLocation baseName = variant == 0 ? texture.Baked.BakedName : texture.Baked.BakedVariants[variant].BakedName;
                    CompositeTexture blended = new CompositeTexture(baseName);
                    if (replaceBaseTexture)
                    {
                        blended = new CompositeTexture(textureOverlays[0].Base);
                        blended.Alpha = textureOverlays[0].Alpha;
                        textureOverlays.RemoveAt(0);
                    }
                    blended.BlendedOverlays = textureOverlays.ToArray();
                    if (capi.EntityTextureAtlas.InsertTextureOrAddReference(blended, out int subId, out _))
                    {
                        mapping[material] = subId;
                        AllocatedTextureBakedNames ??= new(1);
                        AllocatedTextureBakedNames[material] = blended.Baked.BakedName;
                    }
                }

                // Free this one after. That way if they're the same, we only adjust the count and don't free and reallocate the texture.
                if (prevBakedName != null)
                {
                    capi.EntityTextureAtlas.ReleaseTextureReference(prevBakedName);
                }
            }

            if (anyOverlay)
            {
                return new DictionaryTextureSource() { Mapping = mapping, Atlas = capi.EntityTextureAtlas };
            }

            return null;
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (AllocatedTextureBakedNames != null && entity.Api is ICoreClientAPI capi)
            {
                foreach (AssetLocation textureName in AllocatedTextureBakedNames.Values) 
                {
                    capi.EntityTextureAtlas.ReleaseTextureReference(textureName);
                }
                AllocatedTextureBakedNames = null;
            }
        }

        public List<string> ListPhenotype()
        {
            List<string> phenotypeStrings = new();
            foreach (IGeneInterpreter interpreter in AllInterpreters)
            {
                interpreter.ListPhenotype(Genome, entity.WatchedAttributes, phenotypeStrings);
            }
            return phenotypeStrings;
        }

        public static bool EntityIsMale(EntityProperties properties)
        {
            JsonObject? isMale = properties.Attributes?["isMale"];
            if (isMale?.Exists == true) return isMale.AsBool();

            return !properties.Code.Path.Contains("-female");
        }

        /// <summary>
        /// If true, this gene combination causes the animal to die before being born.
        /// This method is for checking when you only have the mother's entity instance available, such as during pregnancy.
        /// </summary>
        public bool IsEmbryonicLethal(Genome genome)
        {
            foreach (IGeneInterpreter interpreter in AllInterpreters)
            {
                if (interpreter.IsEmbryonicLethal(genome))
                {
                    return true;
                }
            }
            return false;
        }

        public override string PropertyName() => Code;
    }
}
