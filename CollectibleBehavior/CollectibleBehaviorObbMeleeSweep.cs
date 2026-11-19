using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public class CollectibleBehaviorObbMeleeSweep : CollectibleBehavior
    {
        readonly OBBMeleeSweepConfig config = new();
        CollectibleBehaviorAnimationAuthoritative? animBeh;
        string[]? selectiveElements;
        string[]? ignoreElements;

        public CollectibleBehaviorObbMeleeSweep(CollectibleObject collObj) : base(collObj) { }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            config.AttachmentPoint = properties["attachmentPoint"].AsString(config.AttachmentPoint);
            config.AutoFitToItemShape = properties["autoFit"].AsBool(config.AutoFitToItemShape);
            var underwaterPenalty = properties["applyUnderwaterPenalty"];
            config.HasUnderwaterPenaltyOverride = underwaterPenalty.Exists;
            config.ApplyUnderwaterPenalty = underwaterPenalty.AsBool(collObj.DamageType != EnumDamageType.PiercingAttack);
            config.LocalMin = ParseVec3f(properties["localMin"], config.LocalMin);
            config.LocalMax = ParseVec3f(properties["localMax"], config.LocalMax);
            config.Grow = ParseVec3f(properties["grow"], config.Grow);
            config.ReachExtension = properties["reach"].AsFloat(config.ReachExtension);
            config.MaxHitsPerSwing = properties["maxHitsPerSwing"].AsInt(config.MaxHitsPerSwing);
            selectiveElements = properties["selectiveElements"].AsArray<string>(null);
            ignoreElements = properties["ignoreElements"].AsArray<string>(null);
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (!api.World.Config.GetBool("obbCombat"))
            {
                return;
            }
            animBeh = collObj.GetCollectibleBehavior<CollectibleBehaviorAnimationAuthoritative>(true);
            if (animBeh == null)
            {
                api.Logger.Error("{0} has the ObbMeleeSweep behavior but no AnimationAuthoritative behavior to drive it, its melee sweep will never run", collObj.Code);
                return;
            }

            animBeh.OnBeginHitEntity += OnBeginHitEntity;
            config.HitFreezeMs = animBeh.HitFreezeMs;

            if (!config.AutoFitToItemShape || selectiveElements == null && ignoreElements == null) return;

            config.CollisionBoxes = [];
            var compositeShape = (collObj as Item)?.Shape ?? (collObj as Block)?.Shape;
            var shapePath = compositeShape?.Base?.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            var shape = shapePath == null ? null : Shape.TryGet(api, shapePath);
            if (shape != null && compositeShape != null)
            {
                config.CollisionBoxes = OBBMath.BuildShapeColliders(shape, compositeShape, selectiveElements, ignoreElements);
                var bounds = new Cuboidd();
                for (int i = 0; i < config.CollisionBoxes.Length; i++)
                {
                    var box = config.CollisionBoxes[i];
                    box.GrowBy(config.Grow.X, config.Grow.Y, config.Grow.Z);
                    if (box is RotatedCuboidd rotated) rotated.GetAABB(bounds);
                    else bounds.Set(box);
                    if (i == 0)
                    {
                        config.LocalMin.Set((float)bounds.X1, (float)bounds.Y1, (float)bounds.Z1);
                        config.LocalMax.Set((float)bounds.X2, (float)bounds.Y2, (float)bounds.Z2);
                    }
                    else
                    {
                        config.LocalMin.Set(GameMath.Min(config.LocalMin.X, (float)bounds.X1), GameMath.Min(config.LocalMin.Y, (float)bounds.Y1), GameMath.Min(config.LocalMin.Z, (float)bounds.Z1));
                        config.LocalMax.Set(GameMath.Max(config.LocalMax.X, (float)bounds.X2), GameMath.Max(config.LocalMax.Y, (float)bounds.Y2), GameMath.Max(config.LocalMax.Z, (float)bounds.Z2));
                    }
                }
            }
            if (config.CollisionBoxes.Length == 0)
            {
                api.Logger.Error("{0} has no matching elements in shape {1}, its melee sweep might not deal damage", collObj.Code, shapePath);
            }
        }

        public OBBMeleeSweepConfig Config => config;

        public override void OnHeldActionAnimStart(ItemSlot slot, EntityAgent byEntity, EnumHandInteract type)
        {
            if (type != EnumHandInteract.HeldItemAttack || animBeh == null) return;
            byEntity.World.Api.ModLoader.GetModSystem<OBBMeleeSweepSystem>()?.BeginSwing(byEntity, slot, config, collObj.GetHeldTpHitAnimation(slot, byEntity));
        }

        void OnBeginHitEntity(EntityAgent byEntity, ref EnumHandling handling)
        {
            var target = (byEntity as EntityPlayer)?.EntitySelection?.Entity;
            if (target?.GetBehavior<EntityBehaviorHitboxGroups>() != null) handling = EnumHandling.PreventDefault;
        }

        static Vec3f ParseVec3f(JsonObject j, Vec3f def)
        {
            if (j == null || !j.Exists) return def;
            var a = j.AsArray<float>(null);
            return a != null && a.Length == 3 ? new Vec3f(a[0], a[1], a[2]) : def;
        }
    }
}
