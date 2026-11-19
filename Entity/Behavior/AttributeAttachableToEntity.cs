using System.Collections.Generic;
using System.Linq;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    /// <summary>
    /// A set of properties that make a collectible attachable to an entity
    /// </summary>
    [DocumentAsJson]
    public class AttributeAttachableToEntity : ILayeredAttachableToEntity
    {
        /// <summary>
        /// Allows this collectible to be attached to slot with specified category code
        /// </summary>
        [DocumentAsJson("Required")]
        public string CategoryCode { get; set; } = "";

        /// <summary>
        /// The shape used when this collectible is attached. If not set, the shape of the collectible is used
        /// </summary>
        [DocumentAsJson("Recommended", "None")]
        public CompositeShape? AttachedShape { get; set; }

        /// <summary>
        /// The textures used when this collectible is attached. Takes priority over collectible textures.
        /// </summary>
        [DocumentAsJson("Recommended", "None")]
        public Dictionary<string, CompositeTexture>? AttachedTextures { get; set; }

        /// <summary>
        /// The textures used when this collectible is attached by shape variant. Takes priority over collectible textures.
        /// </summary>
        [DocumentAsJson("Recommended", "None")]
        public Dictionary<string, Dictionary<string, CompositeTexture>>? AttachedTexturesByVariant { get; set; }

        [DocumentAsJson("Recommended", "None")]
        public Dictionary<string, Dictionary<string, Dictionary<string, CompositeTexture>>>? AttachedTexturesByVariantBySlotCode { get; set; }

        /// <summary>
        /// The shape elements that should not be rendered
        /// </summary>
        [DocumentAsJson("Optional", "None")]
        public string[]? DisableElements { get; set; }

        /// <summary>
        /// The shape elements that should still be rendered even if they are covered by <see cref="DisableElements"/>
        /// </summary>
        [DocumentAsJson("Optional", "None")]
        public string[]? KeepElements { get; set; }

        /// <summary>
        /// Unique key for this attachable to make sure it is rendered correctly
        /// </summary>
        [DocumentAsJson("Recommended", "None")]
        public string? TexturePrefixCode { get; set; }

        public string? GetTexturePrefixCode(ItemStack stack) => TexturePrefixCode;

        /// <summary>
        /// The shape used when this collectible is attached to specific slot
        /// </summary>
        [DocumentAsJson("Recommended", "None")]
        public API.Datastructures.OrderedDictionary<string, CompositeShape>? AttachedShapeBySlotCode { get; set; }

        /// <summary>
        /// The textures used when this collectible is attached to specific slot. Takes priority over collectible textures.
        /// </summary>
        [DocumentAsJson("Recommended", "None")]
        public API.Datastructures.OrderedDictionary<string, Dictionary<string, CompositeTexture>>? AttachedTexturesBySlotCode { get; set; }

        [DocumentAsJson("Recommended", "None")]
        public API.Datastructures.OrderedDictionary<string, CompositeShape>? AttachedShapeByVariant { get; set; }

        [DocumentAsJson("Recommended", "None")]
        public API.Datastructures.OrderedDictionary<string, API.Datastructures.OrderedDictionary<string, CompositeShape>>? AttachedShapeByVariantBySlotCode { get; set; }


        public void CollectTextures(ItemStack stack, Shape gearShape, string texturePrefixCode, Dictionary<string, CompositeTexture> intoDict) => IAttachableToEntity.CollectTexturesFromCollectible(stack, texturePrefixCode, gearShape, intoDict);

        /// <summary>
        /// Occupy additional space behind this slot
        /// </summary>
        [DocumentAsJson("Optional", "0")]
        public int RequiresBehindSlots { get; set; }


        public CompositeShape GetAttachedShape(ItemStack stack, Entity forEntity, string slotCode) => GetAttachedShape(stack, forEntity, slotCode, null);

        public CompositeShape GetAttachedShape(ItemStack stack, Entity forEntity, string slotCode, string? layer)
        {
            if (AttachedShapeByVariantBySlotCode != null)
            {
                string? currentShapeVariant = stack.Attributes?.GetString("shapeVariant");
                if (currentShapeVariant != null)
                {
                    foreach ((string shapeSlotCode, var attachedShapeByVariant) in AttachedShapeByVariantBySlotCode)
                    {
                        if (!WildcardUtil.Match(shapeSlotCode, slotCode)) continue;
                        
                        foreach ((string shapeVariant, CompositeShape attachedShape) in attachedShapeByVariant)
                        {
                            if (!WildcardUtil.Match(shapeVariant, currentShapeVariant)) continue;
                            
                            return resolve(attachedShape, stack, forEntity, layer, slotCode);
                        }
                    }
                }
            }

            if (AttachedShapeByVariant != null)
            {
                string? currentShapeVariant = stack.Attributes?.GetString("shapeVariant");
                if (currentShapeVariant != null)
                {
                    foreach ((string shapeSlotCode, CompositeShape attachedShape) in AttachedShapeByVariant)
                    {
                        if (WildcardUtil.Match(shapeSlotCode, currentShapeVariant))
                        {
                            return resolve(attachedShape, stack, forEntity, layer, slotCode);
                        }
                    }
                }
            }

            if (AttachedShapeBySlotCode != null)
            {
                foreach ((string shapeSlotCode, CompositeShape attachedShape) in AttachedShapeBySlotCode)
                {
                    if (WildcardUtil.Match(shapeSlotCode, slotCode))
                    {
                        return resolve(attachedShape, stack, forEntity, layer, slotCode);
                    }
                }
            }

            if (AttachedShape != null)
            {
                return resolve(AttachedShape, stack, forEntity, layer, slotCode);
            }

            return stack.Class == EnumItemClass.Item ? stack.Item.Shape : stack.Block.Shape;
        }

        public IDictionary<string, CompositeTexture> GetAttachedTextures(ItemStack stack, Entity forEntity, string slotCode) => GetAttachedTextures(stack, forEntity, slotCode, null);

        public IDictionary<string, CompositeTexture> GetAttachedTextures(ItemStack stack, Entity forEntity, string slotCode, string? layer)
        {
            if (AttachedTexturesByVariantBySlotCode != null)
            {
                string? currentShapeVariant = stack.Attributes?.GetString("shapeVariant");
                if (currentShapeVariant != null)
                {
                    foreach ((string shapeSlotCode, var attachedShapeByVariant) in AttachedTexturesByVariantBySlotCode)
                    {
                        if (!WildcardUtil.Match(shapeSlotCode, slotCode)) continue;

                        foreach ((string shapeVariant, var attachedTextures) in attachedShapeByVariant)
                        {
                            if (!WildcardUtil.Match(shapeVariant, currentShapeVariant)) continue;

                            return attachedTextures.ToDictionary(entry => entry.Key, entry => resolve(entry.Value, stack, forEntity, layer, slotCode));
                        }
                    }
                }
            }

            if (AttachedTexturesByVariant != null)
            {
                string? currentShapeVariant = stack.Attributes?.GetString("shapeVariant");
                if (currentShapeVariant != null)
                {
                    foreach ((string shapeSlotCode, var attachedTextures) in AttachedTexturesByVariant)
                    {
                        if (WildcardUtil.Match(shapeSlotCode, currentShapeVariant))
                        {
                            return attachedTextures.ToDictionary(entry => entry.Key, entry => resolve(entry.Value, stack, forEntity, layer, slotCode));
                        }
                    }
                }
            }

            if (AttachedTexturesBySlotCode != null)
            {
                foreach ((string shapeSlotCode, var attachedTextures) in AttachedTexturesBySlotCode)
                {
                    if (WildcardUtil.Match(shapeSlotCode, slotCode))
                    {
                        return attachedTextures.ToDictionary(entry => entry.Key, entry => resolve(entry.Value, stack, forEntity, layer, slotCode));
                    }
                }
            }

            if (AttachedTextures != null)
            {
                Dictionary<string, CompositeTexture> attachedTextureReference = AttachedTextures;

                return AttachedTextures.ToDictionary(entry => entry.Key, entry => resolve(entry.Value, stack, forEntity, layer, slotCode));
            }

            return stack.Class == EnumItemClass.Item ? stack.Item.Textures : stack.Block.Textures;
        }



        protected struct SubstituteValues
        {
            public string? Animal;
            public string? Variant;
            public string? Layer;
            public string? Slot;
            public string? AdditionalVariant;

            public bool NothingToSubstitute() => Animal == null && Variant == null && Layer == null && AdditionalVariant == null;
        }

        protected static CompositeTexture resolve(CompositeTexture texture, ItemStack stack, Entity forEntity, string? layer = null, string? slot = null, bool clone = true)
        {
            SubstituteValues values = GetSubstituteValues(stack, forEntity, layer, slot);

            if (values.NothingToSubstitute()) return texture;

            CompositeTexture clonedTexture = clone ? texture.Clone() : texture;

            return resolveTexture(clonedTexture, values);
        }

        protected static CompositeShape resolve(CompositeShape shape, ItemStack stack, Entity forEntity, string? layer = null, string? slot = null, bool clone = true)
        {
            SubstituteValues values = GetSubstituteValues(stack, forEntity, layer, slot);

            if (values.NothingToSubstitute()) return shape;

            CompositeShape clonedShape = clone ? shape.Clone() : shape;

            return resolveShape(clonedShape, values);
        }

        protected static SubstituteValues GetSubstituteValues(ItemStack stack, Entity forEntity, string? layer = null, string? slot = null)
        {
            API.Datastructures.OrderedDictionary<string, string>? animalMapping = stack.ItemAttributes["animalMapping"]?.AsObject<API.Datastructures.OrderedDictionary<string, string>>();
            string? animalMappedValue = null;
            if (animalMapping != null)
            {
                foreach ((string animalWildcard, string mappedValue) in animalMapping)
                {
                    if (WildcardUtil.Match(animalWildcard, forEntity.Code.Path))
                    {
                        animalMappedValue = mappedValue;
                        break;
                    }
                }
            }

            string? shapeVariant = stack.Attributes?.GetString("shapeVariant", "");
            string? textureVariant = stack.Attributes?.GetString("textureVariant", "");

            return new()
            {
                Animal = animalMappedValue,
                Variant = shapeVariant,
                Layer = layer,
                Slot = slot,
                AdditionalVariant = textureVariant
            };
        }

        protected static CompositeShape resolveShape(CompositeShape shape, SubstituteValues values)
        {
            shape.Base = resolveLocation(shape.Base, values);

            if (shape.Alternates != null && shape.Alternates.Length > 0)
            {
                shape.Alternates = shape.Alternates.Select(alternate => resolveShape(alternate, values)).ToArray();
            }

            if (shape.Overlays != null && shape.Overlays.Length > 0)
            {
                shape.Overlays = shape.Overlays.Select(overlay => resolveShape(overlay, values)).ToArray();
            }

            return shape;
        }

        protected static CompositeTexture resolveTexture(CompositeTexture texture, SubstituteValues values)
        {
            texture.Base = resolveLocation(texture.Base, values);

            if (texture.Tiles != null && texture.Tiles.Length > 0)
            {
                texture.Tiles = texture.Tiles.Select(tile => resolveTexture(tile, values)).ToArray();
            }

            if (texture.Alternates != null && texture.Alternates.Length > 0)
            {
                texture.Alternates = texture.Alternates.Select(alternate => resolveTexture(alternate, values)).ToArray();
            }

            if (texture.BlendedOverlays != null && texture.BlendedOverlays.Length > 0)
            {
                texture.BlendedOverlays = texture.BlendedOverlays.Select(overlay => resolveBlendedOverlay(overlay, values)).ToArray();
            }

            return texture;
        }

        protected static BlendedOverlayTexture resolveBlendedOverlay(BlendedOverlayTexture texture, SubstituteValues values)
        {
            texture.Base = resolveLocation(texture.Base, values);

            return texture;
        }

        protected static AssetLocation resolveLocation(AssetLocation location, SubstituteValues values)
        {
            if (values.Animal != null && location.Path.Contains("{animal}"))
            {
                location.Path = location.Path.Replace("{animal}", values.Animal);
            }

            if (values.Variant != null && location.Path.Contains("{variant}"))
            {
                location.Path = location.Path.Replace("{variant}", values.Variant);
            }

            if (values.AdditionalVariant != null && location.Path.Contains("{additionalvariant}"))
            {
                location.Path = location.Path.Replace("{additionalvariant}", values.AdditionalVariant);
            }

            if (values.Variant != null && location.Path.Contains("{shapevariant}"))
            {
                location.Path = location.Path.Replace("{shapeVariant}", values.Variant);
            }

            if (values.AdditionalVariant != null && location.Path.Contains("{texturevariant}"))
            {
                location.Path = location.Path.Replace("{texturevariant}", values.AdditionalVariant);
            }

            if (values.Layer != null && location.Path.Contains("{layer}"))
            {
                location.Path = location.Path.Replace("{layer}", values.Layer);
            }

            if (values.Layer != null && location.Path.Contains("{layercode}"))
            {
                location.Path = location.Path.Replace("{layercode}", values.Layer);
            }

            if (values.Layer != null && location.Path.Contains("{slot}"))
            {
                location.Path = location.Path.Replace("{slot}", values.Slot);
            }

            if (values.Layer != null && location.Path.Contains("{slotcode}"))
            {
                location.Path = location.Path.Replace("{slotcode}", values.Slot);
            }

            return location;
        }

        public string GetCategoryCode(ItemStack stack) => CategoryCode;
        public string[]? GetDisableElements(ItemStack stack) => DisableElements;
        public string[]? GetKeepElements(ItemStack stack) => KeepElements;
        public bool IsAttachable(Entity toEntity, ItemStack itemStack) => true;
    }
}
