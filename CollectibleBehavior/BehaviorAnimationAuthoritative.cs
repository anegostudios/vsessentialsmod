using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.GameContent
{
    public class CollectibleBehaviorAnimationAuthoritative : CollectibleBehavior
    {
        protected AssetLocation strikeSound;
        public EnumHandInteract strikeSoundHandInteract = EnumHandInteract.HeldItemAttack;
        bool onlyOnEntity;
        public bool IsMeleeAttackMove;
        public int HitFreezeMs = 100;

        public delegate void OnBeginHitEntityDelegate(EntityAgent byEntity, ref EnumHandling handling);
        public delegate void OnAttackStartDelegate(ItemSlot slot, EntityAgent byEntity);

        public CollectibleBehaviorAnimationAuthoritative(CollectibleObject collObj) : base(collObj) { }

        public event OnBeginHitEntityDelegate OnBeginHitEntity;
        public event OnAttackStartDelegate OnAttackStart;


        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            strikeSound = AssetLocation.Create(properties["strikeSound"].AsString("sounds/player/strike"), collObj.Code.Domain);
            onlyOnEntity = properties["onlyOnEntity"].AsBool(false);
            IsMeleeAttackMove = properties["isMeleeAtackMove"].AsBool(false);
            HitFreezeMs = properties["hitFreezeMs"].AsInt(HitFreezeMs);
        }

        public static float getHitDamageAtFrame(EntityAgent byEntity, string animCode)
        {
            if (byEntity.Properties.Client.AnimationsByMetaCode.TryGetValue(animCode, out var animdata))
            {
                if (animdata.Attributes?["damageAtFrame"].Exists == true)
                {
                    return animdata.Attributes["damageAtFrame"].AsFloat(-1);
                }

            }
            return -1;
        }
        public static float getSoundAtFrame(EntityAgent byEntity, string animCode)
        {
            if (byEntity.Properties.Client.AnimationsByMetaCode.TryGetValue(animCode, out var animdata))
            {
                if (animdata.Attributes?["soundAtFrame"].Exists == true)
                {
                    return animdata.Attributes["soundAtFrame"].AsFloat(-1);
                }
            }
            return -1;
        }


        public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity byEntity, ref EnumHandling bhHandling)
        {
            bhHandling = EnumHandling.PreventDefault;
            return collObj.HeldTpUseAnimation; // "interactstatic"; - why hardcoded?
        }


        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            OnAttackStart?.Invoke(slot, byEntity);

            if (onlyOnEntity && entitySel == null) return;

            var damageMode = (byEntity is EntityPlayer ? collObj.GetCollectibleInterface<IHeldAttackDamageModes>() : null)?.GetDamageMode(slot.Itemstack);
            if (damageMode == null) StartAttack(slot, byEntity);

            if (byEntity is EntityPlayer player && byEntity.AnimManager is PlayerAnimationManager animManager)
            {
                if (damageMode != null)
                {
                    if (IsMeleeAttackMove) player.IsPerformingMeleeAttack = true;
                    if (byEntity.World.Side == EnumAppSide.Server) player.StageHeldAttackDamageMode(slot, damageMode.DamageType);
                }

                string anim = collObj.GetHeldTpHitAnimation(slot, byEntity);
                if (animManager.IsAuthoritative(anim) &&
                    (!animManager.IsAnimationActive(anim) || animManager.GetAnimationState(anim) is not { Active: true, NowPlaying: true }))
                {
                    player.HeldTpHitAwaitingStart = collObj;
                }
            }
            handling = EnumHandling.PreventSubsequent;
            handHandling = EnumHandHandling.PreventDefault;
        }


        public override void OnHeldActionAnimStart(ItemSlot slot, EntityAgent byEntity, EnumHandInteract type)
        {
            if (type != EnumHandInteract.HeldItemAttack || byEntity is not EntityPlayer player) return;

            var damageMode = collObj.GetCollectibleInterface<IHeldAttackDamageModes>()?.GetDamageMode(slot.Itemstack);
            if (!ReferenceEquals(player.HeldTpHitAwaitingStart, collObj) &&
                (damageMode == null || onlyOnEntity && player.EntitySelection == null ||
                 player.AnimManager is not PlayerAnimationManager animManager || !animManager.IsHeldHitAuthoritative())) return;

            player.HeldTpHitAwaitingStart = null;
            if (damageMode == null) StartAttack(slot, byEntity);
            else registerAttackCallbacks(slot, byEntity, false);
        }

        public override bool OnHeldAttackCancel(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel, EnumItemUseCancelReason cancelReason, ref EnumHandling handling)
        {
            handling = EnumHandling.PreventSubsequent;
            return false;
        }

        public override bool OnHeldAttackStep(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel, ref EnumHandling handling)
        {
            handling = EnumHandling.PreventSubsequent;
            return StepAttack(slot, byEntity);
        }
            

        public override void OnHeldAttackStop(float secondsPassed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSelection, EntitySelection entitySel, ref EnumHandling handling)
        {
            if (byEntity is EntityPlayer player && ReferenceEquals(player.HeldTpHitAwaitingStart, collObj)) player.HeldTpHitAwaitingStart = null;
            if (IsMeleeAttackMove) (byEntity as EntityPlayer).IsPerformingMeleeAttack = false;
        }


        public void StartAttack(ItemSlot slot, EntityAgent byEntity)
        {
            registerAttackCallbacks(slot, byEntity, true);
        }

        private void registerAttackCallbacks(ItemSlot slot, EntityAgent byEntity, bool startMeleeAttackMove)
        {
            string anim = collObj.GetHeldTpHitAnimation(slot, byEntity);

            byEntity.Attributes.SetInt("didattack", 0);
            if (IsMeleeAttackMove && startMeleeAttackMove) (byEntity as EntityPlayer).IsPerformingMeleeAttack = true;

            byEntity.AnimManager.RegisterFrameCallback(new AnimFrameCallback()
            {
                Animation = anim,
                Frame = getSoundAtFrame(byEntity, anim),
                Callback = () => playStrikeSound(byEntity)
            });

            byEntity.AnimManager.RegisterFrameCallback(new AnimFrameCallback()
            {
                Animation = anim,
                Frame = getHitDamageAtFrame(byEntity, anim),
                Callback = () => hitEntity(byEntity)
            });
        }



        public bool StepAttack(ItemSlot slot, EntityAgent byEntity)
        {
            string animCode = collObj.GetHeldTpHitAnimation(slot, byEntity);
            return byEntity.AnimManager.IsAnimationActive(animCode) || byEntity is EntityPlayer player && ReferenceEquals(player.HeldTpHitAwaitingStart, collObj);
        }



        protected virtual void playStrikeSound(EntityAgent byEntity)
        {
            IPlayer byPlayer = (byEntity as EntityPlayer).Player;
            if (byPlayer == null) return;

            if (byEntity.Controls.HandUse == strikeSoundHandInteract)
            {
                byPlayer.Entity.World.PlaySoundAt(strikeSound, byPlayer.Entity, byPlayer, 0.9f + (float)byEntity.World.Rand.NextDouble() * 0.2f, 16, 0.35f);
            }
        }

        public virtual void hitEntity(EntityAgent byEntity)
        {
            EnumHandling handling = EnumHandling.PassThrough;
            OnBeginHitEntity?.Invoke(byEntity, ref handling);
            if (handling != EnumHandling.PassThrough) return;


            var entitySel = (byEntity as EntityPlayer)?.EntitySelection;
            long selectedEntityId = entitySel?.Entity?.EntityId ?? 0;
            long mountEntityId = byEntity?.MountedOn?.Entity?.EntityId ?? 0;

            if (byEntity.World.Side == EnumAppSide.Client)
            {
                IClientWorldAccessor world = byEntity.World as IClientWorldAccessor;

                if (byEntity.Attributes.GetInt("didattack") == 0)
                {
                    if (entitySel != null && selectedEntityId != mountEntityId)
                    {
                        EntityPlayer player = (EntityPlayer)byEntity;
                        var slot = player.RightHandItemSlot;
                        var modes = collObj.GetCollectibleInterface<IHeldAttackDamageModes>();
                        if (modes != null &&
                            (slot?.Itemstack?.Collectible != collObj ||
                             modes.GetDamageMode(slot.Itemstack) != null && player.GetHeldAttackDamageType(slot) == null)) return;

                        player.HitFreezeRequestMs = HitFreezeMs;
                        world.TryAttackEntity(entitySel);
                        player.HitFreezeRequestMs = 0;
                    }
                    byEntity.Attributes.SetInt("didattack", 1);
                }
            }
            else
            {
                if (byEntity.Attributes.GetInt("didattack") == 0 && entitySel != null && selectedEntityId != mountEntityId)
                {
                    byEntity.Attributes.SetInt("didattack", 1);
                }
            }
        }
    }
}
