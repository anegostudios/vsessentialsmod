using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Vintagestory.GameContent
{
    [ProtoContract]
    public class OBBHitReport
    {
        [ProtoMember(1)] public float[]? WorldMatrix;
        [ProtoMember(2)] public Vec3f? LocalMin;
        [ProtoMember(3)] public Vec3f? LocalMax;
        [ProtoMember(4)] public Dictionary<long, string>? HitBones;
    }

    public class OBBMeleeSweepConfig
    {
        public string AttachmentPoint = "RightHand";
        public bool AutoFitToItemShape = true;
        public Vec3f LocalMin = new(0, 0, 0);
        public Vec3f LocalMax = new(1, 1, 1);
        public Vec3f Grow = new(0.2f, 0.2f, 0.2f);
        public float ReachExtension = 1.0f;
        public int MaxHitsPerSwing = 8;
        public int HitFreezeMs = 100;
        public EnumDamageType DamageType = EnumDamageType.BluntAttack;
        public bool ApplyUnderwaterPenalty = true;
        public bool HasUnderwaterPenaltyOverride;
        public Cuboidd[]? CollisionBoxes;
    }

    public class OBBMeleeSweepSystem : ModSystem
    {
        public const string ChannelId = "obbmeleesweep";

        IClientNetworkChannel? clientChannel;

        public MeleeHitDebugSystem? HitDebug { get; private set; }

        readonly Dictionary<long, OBBMeleeSweep> activeSweeps = new();

        public override bool ShouldLoad(ICoreAPI api) => api.World.Config.GetBool("obbCombat");

        public override void Start(ICoreAPI api)
        {
            api.Network.RegisterChannel(ChannelId).RegisterMessageType(typeof(OBBHitReport));
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            clientChannel = capi.Network.GetChannel(ChannelId);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            sapi.Network.GetChannel(ChannelId).SetMessageHandler<OBBHitReport>(OnClientHitReport);
            HitDebug = sapi.ModLoader.GetModSystem<MeleeHitDebugSystem>();
        }

        public OBBMeleeSweep BeginSwing(Entity attacker, ItemSlot? weaponSlot, OBBMeleeSweepConfig config, string? attackAnimation)
        {
            if (activeSweeps.TryGetValue(attacker.EntityId, out var existing)) existing.End();

            var sweep = new OBBMeleeSweep(this, attacker, weaponSlot, config, attackAnimation);
            activeSweeps[attacker.EntityId] = sweep;
            sweep.Begin();
            return sweep;
        }

        public OBBMeleeSweep? GetActiveSweep(long entityId) => activeSweeps.TryGetValue(entityId, out var s) ? s : null;

        internal void OnSweepEnded(OBBMeleeSweep sweep)
        {
            activeSweeps.Remove(sweep.Attacker.EntityId);
        }

        internal void ReportClientOBB(float[] worldMatrix, Vec3f localMin, Vec3f localMax, Dictionary<long, string> hitBones)
        {
            clientChannel?.SendPacket(new OBBHitReport { WorldMatrix = worldMatrix, LocalMin = localMin, LocalMax = localMax, HitBones = hitBones });
        }

        void OnClientHitReport(IServerPlayer player, OBBHitReport report)
        {
            if (!activeSweeps.TryGetValue(player.Entity.EntityId, out var sweep))
            {
                var slot = player.Entity.RightHandItemSlot;
                var collectible = slot?.Itemstack?.Collectible;
                var beh = collectible?.GetCollectibleBehavior<CollectibleBehaviorObbMeleeSweep>(true);
                if (collectible == null || beh == null) return;

                var attackAnimation = collectible.GetHeldTpHitAnimation(slot, player.Entity);
                if (player.Entity.AnimManager?.IsAnimationActive(attackAnimation) != true) return;

                sweep = BeginSwing(player.Entity, slot, beh.Config, attackAnimation);
            }

            sweep.ApplyClientOBB(report);
        }
    }

    public class OBBMeleeSweep
    {
        const float BroadphasePadding = 2f;
        const float damageFrameWindow = 4f;
        const double ClientBoundsTolerance = 0.001;
        const float reportedBoneTolerance = 0.3f;
        const double minSwingMotionSq = 1e-6;
        const float particleSwingSpeed = 2f;
        const float particleSwingSpread = 0.5f;

        /// <summary>
        /// Metres the client's reported box may sit from the server's own box for the same swing,
        /// covering animation phase difference and latency.
        /// </summary>
        const double MaxAnchorDistance = 2;

        /// <summary>
        /// Factor a client report may exceed the server's own reach and box size by, matching the
        /// desync budget the reach check in ServerSystemEntitySimulation allows.
        /// </summary>
        const double ClientDesyncBudget = 2;

        readonly OBBMeleeSweepSystem system;
        readonly Entity attacker;
        readonly ItemSlot? weaponSlot;
        readonly OBBMeleeSweepConfig config;
        readonly string? attackAnimation;
        readonly bool isClientReporter;
        readonly bool isServerReceiver;
        readonly EnumDamageType? heldAttackDamageType;
        readonly ITreeAttribute? heldAttackTempAttributes;

        readonly HashSet<long> alreadyHit = new();
        long? tickListenerId;
        bool ended;
        bool hasObb;
        bool hasPreviousSwingCenter;

        readonly Matrixf weaponMatrix = new();
        readonly Matrixf clientCandidateMatrix = new();
        readonly Matrixf firstPersonMatrix = new();
        readonly double[] weaponWorldDouble = new double[16];
        readonly Cuboidd weaponLocalBounds = new();
        readonly TransformedCuboidd weaponBox = new();
        readonly TransformedCuboidd expandedWeaponBox = new();
        readonly TransformedCuboidd[] weaponBoxes;
        readonly TransformedCuboidd[] expandedWeaponBoxes;
        readonly double[] childWorld = new double[16];
        readonly Vec3f obbMin = new();
        readonly Vec3f obbMax = new();
        readonly Vec3d sweepCenter = new();
        readonly Vec3d previousRelativeSwingCenter = new();
        readonly Vec3f swingDirection = new();
        readonly Vec3d clientReportCenter = new();
        readonly Vec3d serverAnchorCenter = new();
        readonly Vec3d firstPersonAnchorCenter = new();
        ItemStack? cachedShapeStack;
        Vec3f? cachedShapeMin;
        Vec3f? cachedShapeMax;

        public Entity Attacker => attacker;
        public IReadOnlyList<Cuboidd>? CollisionBoxes => config.CollisionBoxes;

        public bool TryGetCurrentOBB(out Matrixf weaponWorld, out Vec3f localMin, out Vec3f localMax)
        {
            weaponWorld = weaponMatrix;
            localMin = obbMin;
            localMax = obbMax;
            return hasObb;
        }

        /// <summary>
        /// Builds the live weapon outline in the supplied entity coordinate space, overwriting thats matrix.
        /// </summary>
        public bool TryGetRenderOBB(Matrixf entityTransform, out Vec3f localMin, out Vec3f localMax)
        {
            localMin = obbMin;
            localMax = obbMax;
            return isClientReporter
                && !ended
                && withinAnimationOBBWindow()
                && TryBuildCurrentOBB(out _, entityTransform);
        }

        public OBBMeleeSweep(OBBMeleeSweepSystem system, Entity attacker, ItemSlot? weaponSlot, OBBMeleeSweepConfig config, string? attackAnimation)
        {
            this.system = system;
            this.attacker = attacker;
            this.weaponSlot = weaponSlot;
            this.config = config;
            this.attackAnimation = attackAnimation;

            if (config.CollisionBoxes == null)
            {
                weaponBoxes = [weaponBox];
                expandedWeaponBoxes = [expandedWeaponBox];
            }
            else
            {
                int boxCount = config.CollisionBoxes.Length;
                weaponBoxes = new TransformedCuboidd[boxCount];
                expandedWeaponBoxes = new TransformedCuboidd[boxCount];
                for (int i = 0; i < boxCount; i++)
                {
                    weaponBoxes[i] = new();
                    expandedWeaponBoxes[i] = new();
                }
            }

            if (attacker is not EntityPlayer player) return;

            isClientReporter = attacker.World.Side == EnumAppSide.Client;
            isServerReceiver = attacker.World.Side == EnumAppSide.Server;

            var stack = weaponSlot?.Itemstack;
            if (stack?.Collectible.GetCollectibleInterface<IHeldAttackDamageModes>()?.GetDamageMode(stack) == null) return;

            heldAttackTempAttributes = stack.TempAttributes;
            if (isClientReporter) heldAttackDamageType = player.GetHeldAttackDamageType(weaponSlot!);
        }

        public void Begin()
        {
            if (isClientReporter && heldAttackTempAttributes != null && heldAttackDamageType == null)
            {
                End();
                return;
            }

            if (!isServerReceiver) Tick();
            tickListenerId = attacker.World.RegisterGameTickListener(OnSweepTick, 20);
        }

        bool hasOriginalWeaponStack()
        {
            return ReferenceEquals(heldAttackTempAttributes, weaponSlot?.Itemstack?.TempAttributes);
        }

        void OnSweepTick(float dt)
        {
            if (attacker.AnimManager?.IsAnimationActive(attackAnimation) != true ||
                heldAttackTempAttributes != null && (weaponSlot != ((EntityPlayer)attacker).RightHandItemSlot || !hasOriginalWeaponStack()))
            {
                End();
                return;
            }
            if (!isServerReceiver) Tick();
        }

        void Tick()
        {
            RefreshAttackerPoses();

            bool withinWindow = withinAnimationOBBWindow();
            if (!withinWindow && !isClientReporter)
            {
                hasObb = false;
                return;
            }
            if (!TryBuildCurrentOBB(out var weaponWorld)) return;
            if (!withinWindow) hasObb = false;
            if (!tryBuildQueryBox(weaponWorld, obbMin, obbMax, sweepCenter, out var halfExtent)) return;
            if (isClientReporter) updateSwingDirection();
            if (!withinWindow) return;

            SweepHits(weaponWorld, halfExtent, damageModeType: heldAttackDamageType);
        }

        bool withinAnimationOBBWindow()
        {
            if (attackAnimation == null) return true;

            var animation = attacker.AnimManager?.GetAnimationState(attackAnimation);
            if (animation == null) return true;
            if (!animation.Active) return false;

            var attributes = animation.meta.Attributes;
            if (attributes == null) return true;

            float damageAtFrame = attributes["damageAtFrame"].AsFloat(-1);
            var startFrame = attributes["obbStartFrame"];
            var endFrame = attributes["obbEndFrame"];

            if ((!startFrame.Exists || !endFrame.Exists) && damageAtFrame < 0) return true;

            return animation.CurrentFrame >= startFrame.AsFloat(damageAtFrame - damageFrameWindow)
                && animation.CurrentFrame <= endFrame.AsFloat(damageAtFrame + damageFrameWindow);
        }

        /// <summary>
        /// Poses the attacker on the server: its bone and attachment-point matrices place the weapon OBB,
        /// and creatures swinging a body part are not posed per tick the way players are.
        /// </summary>
        void RefreshAttackerPoses()
        {
            if (attacker.World.Side != EnumAppSide.Server) return;
            (attacker.AnimManager?.Animator as AnimatorBase)?.RecalculateMatrices();
        }

        public void ApplyClientOBB(OBBHitReport report)
        {
            var hitBones = report.HitBones;
            if (!isServerReceiver || alreadyHit.Count >= config.MaxHitsPerSwing || hitBones == null) return;

            EnumDamageType? damageModeType = null;
            if (heldAttackTempAttributes != null)
            {
                if (!hasOriginalWeaponStack()) return;
                damageModeType = ((EntityPlayer)attacker).GetHeldAttackDamageType(weaponSlot);
                if (damageModeType == null) return;
            }

            if (!TryAcceptClientOBB(report, out var weaponWorld)) return;
            SweepHits(weaponWorld, hitBones, damageModeType);
        }

        public void End()
        {
            if (ended) return;
            ended = true;
            hasObb = false;
            if (tickListenerId != null)
            {
                attacker.World.UnregisterGameTickListener((long)tickListenerId);
                tickListenerId = null;
            }
            system.OnSweepEnded(this);
        }

        bool TryBuildCurrentOBB([NotNullWhen(true)] out Matrixf? weaponWorld, Matrixf? entityTransform = null)
        {
            weaponWorld = null;
            if (config.CollisionBoxes?.Length == 0) return false;

            Vec3f localMin = config.LocalMin, localMax = config.LocalMax;
            bool autoFit = config.CollisionBoxes != null;

            if (!autoFit && config.AutoFitToItemShape && TryResolveShapeBounds(out var shapeMin, out var shapeMax))
            {
                localMin = shapeMin;
                localMax = shapeMax;
                autoFit = true;
            }

            bool built;
            if (entityTransform == null)
            {
                built = autoFit
                    ? OBBMath.TryBuildItemShapeWorldMatrix(attacker, config.AttachmentPoint, out weaponWorld, weaponMatrix)
                    : OBBMath.TryBuildWeaponWorldMatrix(attacker, config.AttachmentPoint, out weaponWorld, weaponMatrix);
            }
            else
            {
                built = autoFit
                    ? OBBMath.TryBuildItemShapeWorldMatrix(attacker, config.AttachmentPoint, entityTransform, out weaponWorld)
                    : OBBMath.TryBuildWeaponWorldMatrix(attacker, config.AttachmentPoint, entityTransform, out weaponWorld);
            }
            if (!built) return false;

            obbMin.Set(localMin);
            obbMax.Set(localMax);
            if (config.CollisionBoxes == null)
            {
                obbMin.Sub(config.Grow);
                obbMax.Add(config.Grow);
                if (autoFit) ExtendStrikeReach();
            }

            if (entityTransform == null) hasObb = true;
            return true;
        }

        bool TryResolveShapeBounds([NotNullWhen(true)] out Vec3f? min, [NotNullWhen(true)] out Vec3f? max)
        {
            var itemStack = (attacker as EntityAgent)?.RightHandItemSlot?.Itemstack;
            if (!ReferenceEquals(itemStack, cachedShapeStack))
            {
                cachedShapeStack = itemStack;
                cachedShapeMin = cachedShapeMax = null;

                var basePath = itemStack?.Item?.Shape?.Base ?? itemStack?.Block?.Shape?.Base;
                if (basePath != null)
                {
                    var shapePath = basePath.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                    if (!OBBMath.TryGetShapeBounds(attacker.World.Api, shapePath, out cachedShapeMin, out cachedShapeMax)
                        && attacker.World.Side == EnumAppSide.Client)
                        attacker.World.Logger.Warning($"OBB melee sweep: could not fit weapon shape '{shapePath}' for {itemStack?.Collectible?.Code}; using fallback box");
                }
                else if (attacker.World.Side == EnumAppSide.Client)
                    attacker.World.Logger.Warning($"OBB melee sweep: held item {itemStack?.Collectible?.Code} has no shape to auto-fit; using fallback box");
            }

            min = cachedShapeMin;
            max = cachedShapeMax;
            return min != null && max != null;
        }

        /// <summary>
        /// Pads the far face of the box's longest local axis so strikes land slightly beyond the
        /// model. Weapons are modelled grip-at-origin, so that axis approximates the swing
        /// direction; set reach to 0 on models where it does not.
        /// </summary>
        void ExtendStrikeReach()
        {
            if (config.ReachExtension <= 0) return;
            float ex = obbMax.X - obbMin.X, ey = obbMax.Y - obbMin.Y, ez = obbMax.Z - obbMin.Z;
            if (ex >= ey && ex >= ez) obbMax.X += config.ReachExtension;
            else if (ey >= ez) obbMax.Y += config.ReachExtension;
            else obbMax.Z += config.ReachExtension;
        }

        void updateSwingDirection()
        {
            double x = sweepCenter.X - attacker.Pos.X;
            double y = sweepCenter.Y - attacker.Pos.InternalY;
            double z = sweepCenter.Z - attacker.Pos.Z;

            if (hasPreviousSwingCenter)
            {
                double dx = x - previousRelativeSwingCenter.X;
                double dy = y - previousRelativeSwingCenter.Y;
                double dz = z - previousRelativeSwingCenter.Z;
                double lengthSq = dx * dx + dy * dy + dz * dz;
                if (lengthSq > minSwingMotionSq)
                {
                    swingDirection.Set((float)dx, (float)dy, (float)dz).Mul((float)(1 / Math.Sqrt(lengthSq)));
                }
            }

            previousRelativeSwingCenter.Set(x, y, z);
            hasPreviousSwingCenter = true;
        }

        void SweepHits(Matrixf weaponWorld, Dictionary<long, string> reportedHitBones, EnumDamageType? damageModeType)
        {
            if (!tryBuildQueryBox(weaponWorld, obbMin, obbMax, sweepCenter, out var halfExtent)) return;
            SweepHits(weaponWorld, halfExtent, reportedHitBones, damageModeType);
        }

        void SweepHits(Matrixf weaponWorld, double halfExtent, Dictionary<long, string>? reportedHitBones = null, EnumDamageType? damageModeType = null)
        {
            if (config.CollisionBoxes != null)
            {
                for (int i = 0; i < config.CollisionBoxes.Length; i++)
                {
                    var box = config.CollisionBoxes[i];
                    var transform = weaponWorldDouble;
                    if (box is RotatedCuboidd rotated)
                    {
                        Mat4d.Mul(childWorld, weaponWorldDouble, rotated.GetRotationMatrix());
                        transform = childWorld;
                    }
                    weaponLocalBounds.Set(box);
                    weaponBoxes[i].Set(weaponLocalBounds, transform);
                    if (reportedHitBones != null)
                    {
                        weaponLocalBounds.GrowBy(reportedBoneTolerance, reportedBoneTolerance, reportedBoneTolerance);
                        expandedWeaponBoxes[i].Set(weaponLocalBounds, transform);
                    }
                }
            }
            else if (reportedHitBones != null)
            {
                weaponLocalBounds.Set(obbMin, obbMax).GrowBy(reportedBoneTolerance, reportedBoneTolerance, reportedBoneTolerance);
                expandedWeaponBox.Set(weaponLocalBounds, weaponWorldDouble);
            }

            var radius = (float)halfExtent + BroadphasePadding;
            var nearby = attacker.World.GetEntitiesAround(sweepCenter, radius, radius);
            Dictionary<long, string>? hitBones = null;

            foreach (var ent in nearby)
            {
                if (!isClientReporter && alreadyHit.Count >= config.MaxHitsPerSwing) break;
                if (ent.EntityId == attacker.EntityId || ent is IProjectile) continue;
                // Clients keep reporting an overlap for as long as it lasts, since the server may reject a report transiently; the server-side set keeps damage single-application
                if (!isClientReporter && alreadyHit.Contains(ent.EntityId)) continue;
                if (ent.GetInterface<IMountable>()?.Controller == attacker) continue;

                var hitboxes = ent.GetBehavior<EntityBehaviorHitboxGroups>();
                if (hitboxes == null) continue;
                HitboxGroup? group = null;
                BoneHitbox? bone = null;
                TransformedCuboidd? hitBox = null;
                if (reportedHitBones != null)
                {
                    if (!reportedHitBones.TryGetValue(ent.EntityId, out var boneName)) continue;
                    if (boneName != null) group = hitboxes.GetHitGroup(expandedWeaponBoxes, out bone, out hitBox, boneName);
                }
                group ??= hitboxes.GetHitGroup(weaponBoxes, out bone, out hitBox);
                if (group == null || bone == null || hitBox == null) continue;

                if (reportedHitBones != null)
                {
                    // Because contact markers use the strike geometry without the report tolerance
                    int partIndex = Array.IndexOf(expandedWeaponBoxes, hitBox);
                    if (partIndex >= 0) hitBox = weaponBoxes[partIndex];
                }

                // Running the same damage path locally to avoid latency and keep hitfreeze precise.
                if (isClientReporter)
                {
                    hitBones ??= new();
                    hitBones[ent.EntityId] = bone.ElementName;
                    if (alreadyHit.Count < config.MaxHitsPerSwing
                        && alreadyHit.Add(ent.EntityId))
                    {
                        applyDamage(hitboxes, bone, group.DamageMultiplier, hitBox, damageModeType);
                    }
                }
                else
                {
                    alreadyHit.Add(ent.EntityId);
                    applyDamage(hitboxes, bone, group.DamageMultiplier, hitBox, damageModeType);
                }
            }

            if (hitBones != null)
            {
                system.ReportClientOBB(weaponWorld.Values, obbMin, obbMax, hitBones);
            }
        }

        private void applyDamage(EntityBehaviorHitboxGroups hitboxes, BoneHitbox bone, float multiplier, TransformedCuboidd hitBox, EnumDamageType? damageModeType)
        {
            var target = hitboxes.entity;
            var slot = weaponSlot ?? (attacker as EntityAgent)?.RightHandItemSlot;
            var stack = slot?.Itemstack;
            var damageType = damageModeType ?? stack?.Collectible.GetDamageType(stack) ?? config.DamageType;
            // Capture the hit before damage callbacks can change the target's pose.
            system.HitDebug?.RecordHit(hitboxes, bone, hitBox, damageType, weaponBoxes);
            Vec3d? damageParticlePosition = null;
            if (attacker.Api is ICoreClientAPI
                && target.Properties.Attributes?["spawnObbDamageParticles"].AsBool() == true
                && hitboxes.TryGetHitPosition(hitBox, bone, out var hitPosition))
            {
                damageParticlePosition = new(hitPosition.X, hitPosition.Y, hitPosition.Z);
            }
            float damage = stack?.Collectible.GetAttackPower(stack) ?? 0.5f;
            damage *= attacker.Stats.GetBlended("meleeWeaponsDamage");

            if (target.Properties.Attributes?["isMechanical"].AsBool() == true)
            {
                damage *= attacker.Stats.GetBlended("mechanicalsDamage");
            }

            if (attacker.Swimming)
            {
                damage *= (float)attacker.World.Config.GetDecimal("underwaterDamageMultiplier", 0.7);

                if (!attacker.OnGround)
                {
                    damage *= (float)attacker.World.Config.GetDecimal("underwaterFloatingDamageMultiplier", 0.8);
                }

                bool applyUnderwaterPenalty = damageModeType == null || config.HasUnderwaterPenaltyOverride
                    ? config.ApplyUnderwaterPenalty
                    : damageType != EnumDamageType.PiercingAttack;
                if (applyUnderwaterPenalty)
                {
                    damage *= (float)attacker.World.Config.GetDecimal("underwaterSevereDamageMultiplier", 0.2);
                }
            }

            bool isCriticalHit = false;
            damage = stack?.Collectible.GetDamageToEntity(damage, target, stack, ref isCriticalHit) ?? damage;

            var damageSource = new DamageSource
            {
                Source = (attacker as EntityPlayer)?.Player != null ? EnumDamageSource.Player : EnumDamageSource.Entity,
                SourceEntity = attacker,
                Type = damageType,
                DamageTier = stack == null ? 0 : slot == null ? stack.Collectible.ToolTier : stack.Collectible.GetToolTier(slot),
                KnockbackStrength = stack?.Collectible.GetKnockbackStrength(stack) ?? 1f
            };

            if (attacker is EntityPlayer && !target.IsActivityRunning("invulnerable"))
            {
                stack?.Collectible.OnAttackingWith(attacker.World, attacker, target, slot);
            }

            bool didDamage = target.ReceiveDamage(damageSource, damage * multiplier);
            if (didDamage) spawnDamageParticles(attacker.Api as ICoreClientAPI, target, damageParticlePosition, damageType, damage * multiplier);
            if (didDamage
                && attacker is EntityAgent attackerAgent
                && target is EntityAgent targetAgent)
            {
                attackerAgent.DidAttack(damageSource, targetAgent);
            }
            if (didDamage && attacker is EntityPlayer player && attackAnimation != null)
            {
                player.StartHitFreeze(attackAnimation, config.HitFreezeMs);
            }
        }

        void spawnDamageParticles(ICoreClientAPI? capi, Entity target, Vec3d? pos, EnumDamageType damageType, float damage)
        {
            if (capi == null || pos == null) return;

            string? textureCode = target.Properties.Attributes?["obbDamageParticleTexture"].AsString();
            ITextureAtlasAPI textureAtlas = capi.EntityTextureAtlas;
            int textureSubId = target.Properties.Client.FirstTexture.Baked.TextureSubId;

            if (!string.IsNullOrEmpty(textureCode))
            {
                var texture = AssetLocation.Create(textureCode, target.Code.Domain);
                var textureAsset = texture.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png");
                if (capi.Assets.Exists(textureAsset)
                    && capi.BlockTextureAtlas.GetOrInsertTexture(texture, out int customTextureSubId, out _))
                {
                    textureAtlas = capi.BlockTextureAtlas;
                    textureSubId = customTextureSubId;
                }
            }

            (float speed, float spread) = damageType switch
            {
                EnumDamageType.BluntAttack => (1f, 2f),
                EnumDamageType.SlashingAttack => (2f, 0.20f),
                EnumDamageType.PiercingAttack => (2f, 0.10f),
                _ => (particleSwingSpeed, particleSwingSpread)
            };

            // Scaling particle count from 10 to 50 based on damage dealt from 10% to 50%
            int particleCount = 10;
            float maxHealth = target.GetBehavior<EntityBehaviorHealth>()?.MaxHealth ?? 0;
            if (maxHealth > 0)
            {
                particleCount = (int)(GameMath.Clamp(damage / maxHealth * 100f, 10f, 50f) + 0.5f);
            }

            var velocity = new Vec3f();
            bool directional = swingDirection.LengthSquared() > 0;
            var rand = attacker.World.Rand;

            for (int i = 0; i < particleCount; i++)
            {
                int color = textureAtlas.GetRandomColor(textureSubId);
                if (directional)
                {
                    velocity.Set(swingDirection)
                        .Mul(speed)
                        .Add(spread, rand.NextSingle() * spread, spread)
                        .Add(-rand.NextSingle() * spread * 2, 0, -rand.NextSingle() * spread * 2);
                }
                else velocity.Set(1f, 0, 1f).Add(-2 * rand.NextSingle(), 2 * rand.NextSingle(), -2 * rand.NextSingle());

                attacker.World.SpawnParticles(
                    1, color,
                    pos.SubCopy(0.15, 0.15, 0.15),
                    pos.AddCopy(0.15, 0.15, 0.15),
                    velocity, velocity, 1.5f, 1f,
                    0.25f + rand.NextSingle() * 0.25f,
                    EnumParticleModel.Cube
                );
            }
        }

        bool TryAcceptClientOBB(OBBHitReport report, [NotNullWhen(true)] out Matrixf? weaponWorld)
        {
            weaponWorld = null;

            RefreshAttackerPoses();

            var reportMatrix = report.WorldMatrix;
            var reportMin = report.LocalMin;
            var reportMax = report.LocalMax;
            if (reportMatrix?.Length != 16 || reportMin == null || reportMax == null) return false;

            if (!TryBuildCurrentOBB(out var serverWeaponWorld)) return false;
            if (!reportMin.Equals(obbMin, ClientBoundsTolerance) || !reportMax.Equals(obbMax, ClientBoundsTolerance)) return false;

            clientCandidateMatrix.Set(reportMatrix);
            if (!tryBuildQueryBox(clientCandidateMatrix, reportMin, reportMax, clientReportCenter, out var halfExtent)) return false;
            if (!tryBuildQueryBox(serverWeaponWorld, obbMin, obbMax, serverAnchorCenter, out var serverHalfExtent)) return false;

            if (halfExtent > serverHalfExtent * ClientDesyncBudget) return false;
            if (!IsWithinAnchorDistance(serverAnchorCenter) && !IsAnchoredToFirstPersonView()) return false;

            var stack = weaponSlot?.Itemstack;
            double reach = (stack?.Collectible.GetAttackRange(stack) ?? GlobalConstants.DefaultAttackRange) * ClientDesyncBudget;
            if (clientReportCenter.SquareDistanceTo(
                    attacker.Pos.X + attacker.LocalEyePos.X,
                    attacker.Pos.InternalY + attacker.LocalEyePos.Y,
                    attacker.Pos.Z + attacker.LocalEyePos.Z) > reach * reach)
                return false;

            weaponWorld = clientCandidateMatrix;
            return true;
        }

        bool IsWithinAnchorDistance(Vec3d anchorCenter)
        {
            return clientReportCenter.SquareDistanceTo(anchorCenter) <= MaxAnchorDistance * MaxAnchorDistance;
        }

        bool IsAnchoredToFirstPersonView()
        {
            return TryBuildFirstPersonOBB(out var weaponWorld)
                && tryBuildQueryBox(weaponWorld, obbMin, obbMax, firstPersonAnchorCenter, out _)
                && IsWithinAnchorDistance(firstPersonAnchorCenter);
        }

        bool TryBuildFirstPersonOBB([NotNullWhen(true)] out Matrixf? weaponWorld)
        {
            weaponWorld = null;
            if (attacker is not EntityPlayer player) return false;

            var entityWorld = OBBMath.BuildFirstPersonWorldMatrix(player, firstPersonMatrix);
            return config.CollisionBoxes != null || config.AutoFitToItemShape && TryResolveShapeBounds(out _, out _)
                ? OBBMath.TryBuildItemShapeWorldMatrix(attacker, config.AttachmentPoint, entityWorld, out weaponWorld)
                : OBBMath.TryBuildWeaponWorldMatrix(attacker, config.AttachmentPoint, entityWorld, out weaponWorld);
        }

        private bool tryBuildQueryBox(Matrixf source, Vec3f localMin, Vec3f localMax, Vec3d center, out double halfExtent)
        {
            halfExtent = 0;
            OBBMath.Widen(source.Values, weaponWorldDouble);
            if (Mat4d.Determinant(weaponWorldDouble) == 0) return false;

            weaponLocalBounds.Set(localMin.X, localMin.Y, localMin.Z, localMax.X, localMax.Y, localMax.Z);
            try
            {
                weaponBox.Set(weaponLocalBounds, weaponWorldDouble);
            }
            catch (ArgumentException)
            {
                return false;
            }

            weaponBox.GetCenter(center);
            halfExtent = Math.Max(Math.Max(weaponBox.BoundsX2 - center.X, weaponBox.BoundsY2 - center.Y), weaponBox.BoundsZ2 - center.Z);
            return true;
        }
    }
}
