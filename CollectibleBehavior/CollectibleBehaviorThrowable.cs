using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Newtonsoft.Json;

namespace Vintagestory.GameContent
{
    public class CollectibleBehaviorThrowable : CollectibleBehavior
    {
        [JsonIgnore]
        protected ProjectileJsonConfig config = new();

        public string ThrownProjectileCode = "";
        public AssetLocation ThrownSound = "game:sounds/player/throw";
        public string AimAnimation = "aim";
        public string ThrowAnimation = "throw";
        public float WindupTimeSec = 0.35f;
        public float ProjectileSpeed = 0.5f;
        public float Accuracy = 0.75f;
        public bool RandomThrowYaw = false;
        public float HorizontalOffset = 0.4f;
        public float VerticalOffset = 0.1f;
        public float ForwardOffset = -0.21f;
        public float ParallaxDistance = 20;

        public CollectibleBehaviorThrowable(CollectibleObject collObj) : base(collObj)
        {
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            if (properties.Exists)
            {
                JsonUtil.Populate<CollectibleBehaviorThrowable>(properties.Token, this);
            }

            config = properties.AsObject<ProjectileJsonConfig>()!;
            ArgumentNullException.ThrowIfNull(config);
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            if (byEntity.Controls.ShiftKey) return;

            byEntity.Attributes.SetInt("aiming", 1);
            byEntity.Attributes.SetInt("aimingCancel", 0);
            byEntity.StartAnimation(AimAnimation);

            handHandling = EnumHandHandling.PreventDefault;
            handling = EnumHandling.PreventDefault;
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandling handling)
        {
            handling = EnumHandling.Handled;
            if (byEntity.Attributes.GetInt("aimingCancel") == 1)
            {
                return false;
            }

            return true;
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason, ref EnumHandling handled)
        {
            byEntity.Attributes.SetInt("aiming", 0);
            byEntity.StopAnimation("aim");

            if (cancelReason != EnumItemUseCancelReason.ReleasedMouse)
            {
                byEntity.Attributes.SetInt("aimingCancel", 1);
            }

            return true;
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandling handling)
        {
            if (byEntity.Attributes.GetInt("aimingCancel") == 1) return;

            byEntity.Attributes.SetInt("aiming", 0);
            byEntity.StopAnimation(AimAnimation);

            if (secondsUsed < WindupTimeSec) return;

            ItemStack stack = slot.TakeOut(1);
            slot.MarkDirty();

            IPlayer? byPlayer = (byEntity as EntityPlayer)?.Player;
            byEntity.World.PlaySoundAt(ThrownSound, byEntity, byPlayer, false, 8);

            EntityProperties? type = byEntity.World.GetEntityType(ThrownProjectileCode);
            var entity = byEntity.World.ClassRegistry.CreateEntity(type);
            IProjectile? projectile = entity as IProjectile;
            if (projectile == null)
            {
                byEntity.World.Logger.Warning($"Thrown entity type code {ThrownProjectileCode} does not exist or does not implement IProjectile, will not spawn projectile");
                return;
            }

            if (RandomThrowYaw)
            {
                entity.Pos.Yaw = entity.Pos.Yaw = GameMath.TWOPI * (float)byEntity.World.Rand.NextDouble();
            }
            projectile.SetFromConfig(config);
            projectile.FiredBy = byEntity;
            projectile.ProjectileStack = stack;

            EntityProjectile.SpawnProjectile(entity, byEntity, ProjectileSpeed, Accuracy, VerticalOffset, HorizontalOffset, ForwardOffset, ParallaxDistance);

            byEntity.StartAnimation(ThrowAnimation);

            handling = EnumHandling.PreventDefault;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            dsc.AppendLine(Lang.Get("{0} {1} damage when thrown", config.Damage, Lang.Get(config.DamageType.ToString())));
        }


        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot, ref EnumHandling handling)
        {
            handling = EnumHandling.Handled;
            return [
                new WorldInteraction()
                {
                    ActionLangCode = "heldhelp-throw",
                    MouseButton = EnumMouseButton.Right,
                }
            ];
        }
    }
}
