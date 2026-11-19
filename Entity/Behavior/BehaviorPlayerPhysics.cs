using System;
using System.Collections.Generic;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

#nullable disable
namespace Vintagestory.GameContent;

/// <summary>
/// Client-side player physics.
/// <br/>Uses the "playerphysics" code
/// </summary>
/// <example><code lang="json">
/// "behaviors": [
///  {
///     "code": "playerphysics",
///     "maxWalkableSlopeGrade": 1
///  }
/// ]
/// </code></example>
[DocumentAsJson]
public class EntityBehaviorPlayerPhysics : EntityBehaviorControlledPhysics, IRemotePhysics
{
    private const double stepClearance = 0.03;
    private const int stepClearanceIterations = 12;
    private const double slopeRiseTolerance = 1 / 1024d;
    private const double walkStepUpSpeed = 0.05;
    private const double sprintStepUpSpeed = 0.10;
    private const double sneakStepUpSpeed = 0.025;

    private IPlayer player;
    private IServerPlayer serverPlayer;
    private EntityPlayer entityPlayer;
    private readonly List<Collider> stepColliders = new();
    private readonly Cuboidd stepTestBox = new();
    private readonly Cuboidd stepTargetBox = new();
    private readonly Cuboidd stepLiftBox = new();
    private readonly Cuboidf stepTerrainBox = new();
    private readonly Vec3d stepPosition = new();

    // 60/s client-side updates.
    private const float interval = 1 / 60f;
    private float accum = 0;
    private int currentTick;

    private int prevDimension = 0;
    public const float ClippingToleranceOnDimensionChange = 0.0625f;

    public EntityBehaviorPlayerPhysics(Entity entity) : base(entity)
    {

    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        entityPlayer = entity as EntityPlayer;
        // Note in contrast with BehaviorControlledPhysics we intentionally do not register this as a PhysicsTickable: each player's physics is periodically sent by the player client to the server

        Init();
        SetProperties(properties, attributes);

        if (entity.Api.Side == EnumAppSide.Client)
        {
            smoothStepping = true;

            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "playerphysics");
            initializeStandingColliderState(true);
        }
        else
        {
            initializeStandingColliderState(false);
            EnumHandling handling = EnumHandling.Handled;
            OnReceivedServerPos(true, ref handling);
        }

        entity.PhysicsUpdateWatcher?.Invoke(0, entity.Pos.XYZ);
    }

    public override void SetModules()
    {
        physicsModules.Add(new PModuleWind());
        physicsModules.Add(new PModuleOnGround());
        physicsModules.Add(new PModulePlayerInLiquid(entityPlayer));
        physicsModules.Add(new PModulePlayerInAir());
        physicsModules.Add(new PModuleGravity());
        physicsModules.Add(new PModuleMotionDrag());
        physicsModules.Add(new PModuleKnockback());
    }

    public new void OnReceivedClientPos(int version)
    {
        serverPlayer ??= entityPlayer.Player as IServerPlayer;

        if (version > previousVersion)
        {
            previousVersion = version;
            HandleRemotePhysics(clientInterval, true);
            return;
        }

        HandleRemotePhysics(clientInterval, false);
    }

    public new void HandleRemotePhysics(float dt, bool isTeleport)
    {
        player ??= entityPlayer.Player;

        if (player == null) return;
        var entity = this.entity;

        if (nPos == null)
        {
            nPos = new();
            nPos.Set(entity.Pos);
        }

        var lPos = this.lPos;
        float dtFactor = dt * 60;

        lPos.SetFrom(nPos);
        nPos.Set(entity.Pos);
        lPos.Dimension = entity.Pos.Dimension;

        // Set the last pos to be the same as the next pos when teleporting.
        if (isTeleport)
        {
            lPos.SetFrom(nPos);
        }

        lPos.Motion.X = (nPos.X - lPos.X) / dtFactor;
        lPos.Motion.Y = (nPos.Y - lPos.Y) / dtFactor;
        lPos.Motion.Z = (nPos.Z - lPos.Z) / dtFactor;

        if (lPos.Motion.Length() > 20) lPos.Motion.Set(0, 0, 0);

        // Set client/server motion.
        entity.Pos.Motion.Set(lPos.Motion);

        collisionTester.NewTick(lPos);

        EntityAgent eagent = entity as EntityAgent;
        if (eagent.MountedOn != null)
        {
            clearColliderMotion();
            entity.Swimming = false;
            entity.FeetInLiquid = false;
            entity.OnGround = false;

            if (capi != null)
            {
                entity.Pos.SetPos(eagent.MountedOn.SeatPosition);
            }

            entity.Pos.Motion.X = 0;
            entity.Pos.Motion.Y = 0;
            entity.Pos.Motion.Z = 0;

            // No-clip detection.
            if (sapi != null)
            {
                collisionTester.ApplyTerrainCollision(entity, lPos, dtFactor, ref newPos, 0, 0);
            }

            return;
        }

        SetState(lPos, dt);


        EntityControls controls = eagent.Controls;
        if (controls.NoClip)
        {
            var pos = entity.Pos;

            clearColliderMotion(pos);
            pos.X += pos.Motion.X * dt * 60f;
            pos.Y += pos.Motion.Y * dt * 60f;
            pos.Z += pos.Motion.Z * dt * 60f;
            entity.Swimming = false;
            entity.FeetInLiquid = false;
            entity.OnGround = false;
            controls.Gliding = false;
            return;
        }

        if (sapi != null)
        {
            collisionTester.ApplyTerrainCollision(entity, lPos, dtFactor, ref newPos, 0, 0);
        }

        RemoteMotionAndCollision(lPos, dtFactor);
        ApplyTests(lPos, eagent.Controls, dt, true);
    }


    // Main client physics tick called every frame.
    public override void OnPhysicsTick(float dt)
    {
        SimPhysics(dt, entity.Pos);
    }

    public override void OnGameTick(float deltaTime)
    {
        // Player physics is called only client side, but we still need to call Block.OnEntityInside and other usual server-side AfterPhysicsTick things
        if (entity.World is IServerWorldAccessor)
        {
            callOnEntityInside();
            entity.AfterPhysicsTick?.Invoke();
            syncStandingColliderState();
        }
        else if (capi.World.Player.Entity != entity)
        {
            // Remote players skip SimPhysics, but their animator still reads the controls' move speed.
            player ??= entityPlayer.Player;
            if (player != null)
            {
                entityPlayer.Controls.MovespeedMultiplier = player.WorldData.MoveSpeedMultiplier;
            }

            entity.AfterPhysicsTick?.Invoke();
        }

        // The local client invokes AfterPhysicsTick from OnRenderFrame().
    }



    public void SimPhysics(float dt, EntityPos pos)
    {
        var entity = this.entity;
        if (entity.State != EnumEntityState.Active) return;
        player ??= entityPlayer.Player;
        if (player == null) return;

        EntityAgent eagent = entity as EntityAgent;
        EntityControls controls = eagent.Controls;

        // Set previous pos to be used for camera callback.
        prevPos.Set(pos);
        tmpPos.dimension = pos.Dimension;

        SetState(pos, dt);
        SetPlayerControls(pos, controls, dt);

        // If mounted on something, set position to it and return.
        if (eagent.MountedOn != null)
        {
            clearColliderMotion();
            entity.Swimming = false;
            entity.FeetInLiquid = false;
            entity.OnGround = false;

            pos.SetPos(eagent.MountedOn.SeatPosition);

            pos.Motion.X = 0;
            pos.Motion.Y = 0;
            pos.Motion.Z = 0;
            return;
        }

        moveWithStandingCollider(pos, controls);

        bool colliderMotionSuspended = suspendInheritedColliderMotion(pos);
        MotionAndCollision(pos, controls, dt);
        restoreInheritedColliderMotion(pos, colliderMotionSuspended);
        if (!controls.NoClip)
        {
            inheritStandingColliderMotion(pos, controls);
            collisionTester.NewTick(pos);

            if (prevDimension != pos.Dimension)
            {
                prevDimension = pos.Dimension;
                clearColliderMotion();

                // Dimension changes are allowed a small amount of clipping into terrain, so we need to push out on the client here, we add 20% for rounding/sync errors
                collisionTester.PushOutFromBlocks(entity.World.BlockAccessor, entity, pos.XYZ, ClippingToleranceOnDimensionChange * 1.2f);
            }

            ApplyTests(pos, controls, dt, false);

            // Attempt to stop gliding/flying.
            if (controls.Gliding)
            {
                if (entity.Collided || entity.FeetInLiquid || !entity.Alive || player.WorldData.FreeMove || controls.IsClimbing)
                {
                    controls.GlideSpeed = 0;
                    controls.Gliding = false;
                    controls.IsFlying = false;
                    entityPlayer.WalkPitch = 0;
                }
            }
            else
            {
                controls.GlideSpeed = 0;
            }
        } else
        {
            pos.X += pos.Motion.X * dt * 60f;
            pos.Y += pos.Motion.Y * dt * 60f;
            pos.Z += pos.Motion.Z * dt * 60f;
            entity.Swimming = false;
            entity.FeetInLiquid = false;
            entity.OnGround = false;
            controls.Gliding = false;

            prevDimension = pos.Dimension;   // If NoClip is enabled we don't care about dimension changes either
        }
    }

    public void SetPlayerControls(EntityPos pos, EntityControls controls, float dt)
    {
        IClientWorldAccessor clientWorld = entity.World as IClientWorldAccessor;
        // We pretend the entity is flying to disable gravity so that EntityBehaviorInterpolatePosition system
        // can work better   (see commit 09003c0c)
        controls.IsFlying = player.WorldData.FreeMove || (clientWorld != null && clientWorld.Player.ClientId != player.ClientId) && !controls.IsClimbing;
        controls.NoClip = player.WorldData.NoClip;
        controls.MovespeedMultiplier = player.WorldData.MoveSpeedMultiplier;

        if (controls.Gliding && !controls.IsClimbing)
        {
            controls.IsFlying = true;
        }

        if ((controls.TriesToMove || controls.Gliding) && player is IClientPlayer clientPlayer)
        {
            float prevYaw = pos.Yaw;
            pos.Yaw = (entity.Api as ICoreClientAPI).Input.MouseYaw;

            if (entity.Swimming || controls.Gliding)
            {
                float prevPitch = pos.Pitch;
                pos.Pitch = clientPlayer.CameraPitch;
                controls.CalcMovementVectors(pos, dt);
                pos.Yaw = prevYaw;
                pos.Pitch = prevPitch;
            }
            else
            {
                controls.CalcMovementVectors(pos, dt);
                pos.Yaw = prevYaw;
            }

            float desiredYaw = (float)Math.Atan2(controls.WalkVector.X, controls.WalkVector.Z);
            float yawDist = GameMath.AngleRadDistance(entityPlayer.WalkYaw, desiredYaw);

            entityPlayer.WalkYaw += GameMath.Clamp(yawDist, -6 * dt * GlobalConstants.OverallSpeedMultiplier, 6 * dt * GlobalConstants.OverallSpeedMultiplier);
            entityPlayer.WalkYaw = GameMath.Mod(entityPlayer.WalkYaw, GameMath.TWOPI);

            if (entity.Swimming || controls.Gliding)
            {
                float desiredPitch = -(float)Math.Sin(pos.Pitch);
                float pitchDist = GameMath.AngleRadDistance(entityPlayer.WalkPitch, desiredPitch);
                entityPlayer.WalkPitch += GameMath.Clamp(pitchDist, -2 * dt * GlobalConstants.OverallSpeedMultiplier, 2 * dt * GlobalConstants.OverallSpeedMultiplier);
                entityPlayer.WalkPitch = GameMath.Mod(entityPlayer.WalkPitch, GameMath.TWOPI);
            }
            else
            {
                entityPlayer.WalkPitch = 0;
            }
        }
        else
        {
            if (!entity.Swimming && !controls.Gliding)
            {
                entityPlayer.WalkPitch = 0;
            }
            else if (entity.OnGround && entityPlayer.WalkPitch != 0)
            {
                entityPlayer.WalkPitch = GameMath.Mod(entityPlayer.WalkPitch, GameMath.TWOPI);
                if (entityPlayer.WalkPitch < 0.01f || entityPlayer.WalkPitch > GameMath.PI - 0.01f)   // Without the PI test, the player can backflip 360 degrees, due to WalkPitch starting in the range PI to TWOPI  (typically just fractionally less than TWOPI)
                {
                    entityPlayer.WalkPitch = 0;
                }
                else // Slowly revert player to upright position if feet touched the bottom of water.
                {
                    entityPlayer.WalkPitch -= GameMath.Clamp(entityPlayer.WalkPitch, 0, 1.2f * dt * GlobalConstants.OverallSpeedMultiplier);

                    if (entityPlayer.WalkPitch < 0) entityPlayer.WalkPitch = 0;
                }
            }

            float prevYaw = pos.Yaw;
            controls.CalcMovementVectors(pos, dt);
            pos.Yaw = prevYaw;
        }
    }

    // Do physics every frame on the client.
    public override void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (capi.IsGamePaused) return;

        if (capi.World.Player.Entity != entity)
        {
            smoothStepping = false;
            releaseRemoteStandingColliderRenderer();
            return;
        }

        // Keep camera interpolation in the collider's frame between fixed physics steps, this should help on moving platforms.
        if (entityPlayer.MountedOn == null && player?.WorldData.FreeMove != true)
        {
            moveWithStandingCollider(entityPlayer.Pos, entityPlayer.Controls, prevPos);
        }
        else
        {
            clearColliderMotion(entityPlayer.Pos);
        }

        accum += dt;

        if (accum > 0.5f)
        {
            accum = 0.5f;
        }

        var mountedEntity = entityPlayer.MountedOn?.Entity;
        var mountedCollider = mountedEntity?.GetBehavior<EntityBehaviorKinematicCollider>();
        bool controlsMount = entityPlayer.MountedOn?.MountSupplier.Controller == entityPlayer;
        IPhysicsTickable tickable = null;
        if (controlsMount)
        {
            tickable = mountedEntity?.SidedProperties.Behaviors.Find(b => b is IPhysicsTickable) as IPhysicsTickable;
        }

        while (accum >= interval)
        {
            tickable?.OnPhysicsTick(interval);
            OnPhysicsTick(interval);

            accum -= interval;
            currentTick++;

            // Send position every 4 ticks.
            if (currentTick % 4 == 0)
            {
                if (entityPlayer.EntityId != 0 && entityPlayer.Alive)
                {
                    capi.Network.SendPlayerPositionPacket();
                    if (tickable != null)
                    {
                        capi.Network.SendPlayerMountPositionPacket(mountedEntity);
                    }
                }
            }

            AfterPhysicsTick(interval);
            tickable?.AfterPhysicsTick(interval);
        }

        if (controlsMount) mountedCollider?.UpdateCollider(dt);

        if (entityPlayer.MountedOn != null && mountedCollider != null)
        {
            // The mount mesh and collider use this frame's seat pose, including frames without a physics step!
            entityPlayer.Pos.SetPos(entityPlayer.MountedOn.SeatPosition);
            prevPos.Set(entityPlayer.Pos);
        }

        // For camera, lerps from prevPos to current pos by 1 + accum.
        entity.PhysicsUpdateWatcher?.Invoke(accum, prevPos);
        mountedEntity?.PhysicsUpdateWatcher?.Invoke(accum, prevPos);
    }


    #region Smooth stepping

    /// <summary>
    /// Overriden for smooth stepping
    /// </summary>
    /// <param name="pos"></param>
    /// <param name="moveDelta"></param>
    /// <param name="dtFac"></param>
    /// <param name="controls"></param>
    /// <returns></returns>
    protected override bool HandleSteppingOnBlocks(EntityPos pos, Vec3d moveDelta, float dtFac, EntityControls controls)
    {
        if (!controls.TriesToMove || (!entity.OnGround && !entity.Swimming) || entity.Properties.Habitat == EnumHabitat.Underwater) return false;

        Cuboidd entityCollisionBox = entity.CollisionBox.ToDouble();

        const double max = 0.5;
        double searchBoxLength = max + (controls.Sprint ? 0.25 : controls.Sneak ? 0.05 : 0.2);

        double centerX = entityCollisionBox.MidX;
        double centerZ = entityCollisionBox.MidZ;
        double searchHeight = Math.Max(entityCollisionBox.Y1 + StepHeight, entityCollisionBox.Y2);

        Vec3d walkVec = controls.WalkVector.Clone();
        Vec3d walkVecNormalized = walkVec.Clone().Normalize();

        double outerX = walkVecNormalized.X * searchBoxLength;
        double outerZ = walkVecNormalized.Z * searchBoxLength;
        double entityHalfWidth = entityCollisionBox.HalfExtentX;
        double entityHalfLength = entityCollisionBox.HalfExtentZ;
        outerX += Math.Sign(outerX) * entityHalfWidth;
        outerZ += Math.Sign(outerZ) * entityHalfLength;
        Cuboidd entitySensorBox = new Cuboidd
        {
            X1 = Math.Min(-entityHalfWidth, outerX),
            X2 = Math.Max(entityHalfWidth, outerX),

            Z1 = Math.Min(-entityHalfLength, outerZ),
            Z2 = Math.Max(entityHalfLength, outerZ),

            Y1 = entity.CollisionBox.Y1 - (!entity.CollidedVertically && !controls.Jump ? 0.05 : 0),

            Y2 = searchHeight
        };

        entitySensorBox.Translate(centerX, 0, centerZ);
        entitySensorBox.Translate(pos.X, pos.Y, pos.Z);

        entityCollisionBox.Translate(pos.X, pos.Y, pos.Z);
        List<Cuboidd> steppableBoxes = FindSteppableCollisionboxSmooth(entityCollisionBox, entitySensorBox, moveDelta.Y, walkVec);

        if (steppableBoxes != null && steppableBoxes.Count > 0)
        {
            Vec3d testVec = new();
            Vec2d testMotion = new();

            if (TryStepSmooth(controls, pos, testMotion.Set(walkVec.X, walkVec.Z), dtFac, steppableBoxes, entityCollisionBox)) return true;

            Cuboidd entitySensorBoxXZAligned = entitySensorBox.Clone();
            if (entitySensorBoxXZAligned.Z1 == pos.Z + centerZ)
            {
                entitySensorBoxXZAligned.Z2 = entitySensorBoxXZAligned.Z1;
            }
            else
            {
                entitySensorBoxXZAligned.Z1 = entitySensorBoxXZAligned.Z2;
            }

            if (TryStepSmooth(controls, pos, testMotion.Set(walkVec.X, 0), dtFac, FindSteppableCollisionboxSmooth(entityCollisionBox, entitySensorBoxXZAligned, moveDelta.Y, testVec.Set(walkVec.X, walkVec.Y, 0)), entityCollisionBox)) return true;

            entitySensorBoxXZAligned.Set(entitySensorBox);
            if (entitySensorBoxXZAligned.X1 == pos.X + centerX)
            {
                entitySensorBoxXZAligned.X2 = entitySensorBoxXZAligned.X1;
            }
            else
            {
                entitySensorBoxXZAligned.X1 = entitySensorBoxXZAligned.X2;
            }

            if (TryStepSmooth(controls, pos, testMotion.Set(0, walkVec.Z), dtFac, FindSteppableCollisionboxSmooth(entityCollisionBox, entitySensorBoxXZAligned, moveDelta.Y, testVec.Set(0, walkVec.Y, walkVec.Z)), entityCollisionBox)) return true;
        }

        if (tryStepOnColliders(controls, pos, moveDelta.X, moveDelta.Z, dtFac, out bool shouldTrySeparateAxes)) return true;

        if (controls.IsStepping && tryStepOnColliders(controls, pos, controls.WalkVector.X * dtFac, controls.WalkVector.Z * dtFac, dtFac, out _)) return true;

        if (!shouldTrySeparateAxes) return false;

        if (tryStepOnColliders(controls, pos, moveDelta.X, 0, dtFac, out _)) return true;

        if (tryStepOnColliders(controls, pos, 0, moveDelta.Z, dtFac, out _)) return true;

        return false;
    }

    private bool tryStepOnColliders(EntityControls controls, EntityPos pos, double motionX, double motionZ, float dtFac, out bool shouldTrySeparateAxes)
    {
        shouldTrySeparateAxes = false;
        if (motionX == 0 && motionZ == 0) return false;

        var world = entity.World;
        var entityBox = entity.CollisionBox;
        double targetX = pos.X + motionX;
        double targetZ = pos.Z + motionZ;
        stepTargetBox.SetAndTranslate(entityBox, targetX, pos.Y, targetZ);
        stepTestBox.Set(stepTargetBox);
        stepTestBox.Y2 += StepHeight;
        world.CollisionScene.GetColliders(stepTestBox, pos.Dimension, stepColliders, new CollisionQueryFilter(CollisionLayer.Entity, excludedOwnerEntityId: entity.EntityId));
        if (!intersectsStepColliders(stepTargetBox)) return false;
        shouldTrySeparateAxes = true;

        stepTestBox.SetAndTranslate(entityBox, pos.X, pos.Y - stepClearance - slopeRiseTolerance, pos.Z);
        bool staysOnSameCollisionBox = staysOnStepCollisionBoxes(stepTestBox, stepTargetBox);
        double currentLift = 0;
        if (staysOnSameCollisionBox)
        {
            stepTestBox.SetAndTranslate(entityBox, pos.X, pos.Y, pos.Z);
            if (!tryGetColliderStepLift(stepTestBox, out currentLift)) return false;
        }

        stepTestBox.Set(stepTargetBox);
        if (!tryGetColliderStepLift(stepTestBox, out double targetColliderLift)) return false;

        stepTestBox.Translate(0, targetColliderLift, 0);
        bool walkable = false;
        for (int colliderIndex = 0; colliderIndex < stepColliders.Count && !walkable; colliderIndex++)
        {
            Collider collider = stepColliders[colliderIndex];
            for (int boxIndex = 0; boxIndex < collider.CollisionBoxCount; boxIndex++)
            {
                if (collider.TryGetAABBContact(boxIndex, stepTestBox, slopeRiseTolerance, out CuboidContact contact)
                    && contact.Separation >= -slopeRiseTolerance && CollisionTester.IsWalkableColliderSurface(contact.Normal, MaxWalkableSlopeGrade))
                {
                    walkable = true;
                    break;
                }
            }
        }
        if (!walkable) return false;

        double horizontalDistance = Math.Sqrt(motionX * motionX + motionZ * motionZ);
        double allowedLift = horizontalDistance * MaxWalkableSlopeGrade + slopeRiseTolerance;
        double slopeLift = targetColliderLift - currentLift;
        if (staysOnSameCollisionBox && slopeLift > allowedLift)
        {
            shouldTrySeparateAxes = false;
            return false;
        }

        double targetClearance = staysOnSameCollisionBox ? slopeRiseTolerance : stepClearance;
        double targetLift = targetColliderLift + targetClearance;
        double movementLift = targetLift;
        if (staysOnSameCollisionBox)
        {
            // Preserve the target clearance after the second collision pass applies gravity.
            movementLift += Math.Max(0, -pos.Motion.Y * dtFac);
            if (movementLift > StepHeight) return false;
        }

        stepPosition.Set(targetX, pos.Y + movementLift, targetZ);
        if (collisionTester.IsColliding(world.BlockAccessor, entityBox, stepPosition, pos.Dimension, false)) return false;

        stepTestBox.SetAndTranslate(entityBox, targetX, pos.Y + movementLift, targetZ);
        if (intersectsStepColliders(stepTestBox)) return false;

        double appliedLift = staysOnSameCollisionBox ? movementLift : Math.Min(getStepUpSpeed(controls) * dtFac, targetLift);
        if (!canStepUp(pos, appliedLift)) return false;
        pos.Y += appliedLift;

        collisionTester.ApplyTerrainCollision(entity, pos, dtFac, ref newPos);
        return true;
    }

    private bool canStepUp(EntityPos pos, double lift)
    {
        var world = entity.World;
        stepTerrainBox.Set(entity.CollisionBox);
        stepTerrainBox.Y2 += (float)lift;
        stepPosition.Set(pos.X, pos.Y, pos.Z);
        if (collisionTester.IsColliding(world.BlockAccessor, stepTerrainBox, stepPosition, pos.Dimension, false)) return false;

        stepTestBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);
        stepLiftBox.Set(stepTestBox);
        stepLiftBox.Y2 += lift;
        world.CollisionScene.GetColliders(stepLiftBox, pos.Dimension, stepColliders, new CollisionQueryFilter(CollisionLayer.Entity, excludedOwnerEntityId: entity.EntityId));
        stepLiftBox.Set(stepTestBox);
        stepLiftBox.Translate(0, lift, 0);
        for (int i = 0; i < stepColliders.Count; i++)
        {
            if (stepColliders[i].TrySweepAABB(stepTestBox, 0, lift, 0, out _) || stepColliders[i].Intersects(stepLiftBox)) return false;
        }
        return true;
    }

    private bool tryGetColliderStepLift(Cuboidd collisionBox, out double lift)
    {
        lift = 0;
        for (int colliderIndex = 0; colliderIndex < stepColliders.Count; colliderIndex++)
        {
            Collider collider = stepColliders[colliderIndex];
            for (int boxIndex = 0; boxIndex < collider.CollisionBoxCount; boxIndex++)
            {
                if (!collider.GetCollisionBoxBounds(boxIndex).Intersects(collisionBox)) continue;
                Cuboidd obstacle = collider.GetCollisionBox(boxIndex);
                if (!obstacle.Intersects(collisionBox)) continue;

                // Find the top of each intersected box so a ceiling cannot hide a gap above the step.
                stepLiftBox.Set(collisionBox);
                stepLiftBox.Translate(0, StepHeight, 0);
                if (obstacle.Intersects(stepLiftBox)) return false;

                double lowerLift = 0;
                double upperLift = StepHeight;
                for (int i = 0; i < stepClearanceIterations; i++)
                {
                    double testLift = (lowerLift + upperLift) * 0.5;
                    stepLiftBox.Y1 = collisionBox.Y1 + testLift;
                    stepLiftBox.Y2 = collisionBox.Y2 + testLift;

                    if (obstacle.Intersects(stepLiftBox)) lowerLift = testLift;
                    else upperLift = testLift;
                }
                lift = Math.Max(lift, upperLift);
            }
        }

        stepLiftBox.Set(collisionBox);
        stepLiftBox.Translate(0, lift, 0);
        return !intersectsStepColliders(stepLiftBox);
    }

    private bool intersectsStepColliders(Cuboidd collisionBox)
    {
        for (int i = 0; i < stepColliders.Count; i++)
        {
            if (stepColliders[i].Intersects(collisionBox)) return true;
        }

        return false;
    }

    private bool staysOnStepCollisionBoxes(Cuboidd firstBox, Cuboidd secondBox)
    {
        bool foundContact = false;
        for (int colliderIndex = 0; colliderIndex < stepColliders.Count; colliderIndex++)
        {
            Collider collider = stepColliders[colliderIndex];
            for (int boxIndex = 0; boxIndex < collider.CollisionBoxCount; boxIndex++)
            {
                Cuboidd bounds = collider.GetCollisionBoxBounds(boxIndex);
                if (!bounds.Intersects(secondBox)) continue;

                Cuboidd collisionBox = collider.GetCollisionBox(boxIndex);
                if (!collisionBox.Intersects(secondBox)) continue;
                if (!bounds.Intersects(firstBox) || !collisionBox.Intersects(firstBox)) return false;
                foundContact = true;
            }
        }

        return foundContact;
    }

    private static double getStepUpSpeed(EntityControls controls)
    {
        return controls.Sprint ? sprintStepUpSpeed : controls.Sneak ? sneakStepUpSpeed : walkStepUpSpeed;
    }



    public bool TryStepSmooth(EntityControls controls, EntityPos pos, Vec2d walkVec, float dtFac, List<Cuboidd> steppableBoxes, Cuboidd entityCollisionBox)
    {
        if (steppableBoxes == null || steppableBoxes.Count == 0) return false;

        Vec2d walkVecOrtho = new Vec2d(walkVec.Y, -walkVec.X).Normalize();

        double maxX = Math.Abs(walkVecOrtho.X * 0.3) + 0.001;
        double minX = -maxX;
        double maxZ = Math.Abs(walkVecOrtho.Y * 0.3) + 0.001;
        double minZ = -maxZ;
        Cuboidf col = new((float)minX, entity.CollisionBox.Y1, (float)minZ, (float)maxX, entity.CollisionBox.Y2, (float)maxZ);

        double newYPos = pos.Y;
        foreach (Cuboidd steppableBox in steppableBoxes)
        {
            if (steppableBox.Y2 <= entityCollisionBox.Y1) continue;

            double heightDiff = steppableBox.Y2 - entityCollisionBox.Y1 + stepClearance;
            Vec3d stepPos = new(GameMath.Clamp(newPos.X, steppableBox.MinX, steppableBox.MaxX), newPos.Y + heightDiff, GameMath.Clamp(newPos.Z, steppableBox.MinZ, steppableBox.MaxZ));

            bool canStep = !collisionTester.IsColliding(entity.World, col, stepPos, pos.Dimension, false);

            if (canStep)
            {
                double stepUpSpeed = getStepUpSpeed(controls);
                if (!steppableBox.IntersectsOrTouches(entityCollisionBox))
                {
                    newYPos = Math.Max(newYPos, Math.Min(pos.Y + stepUpSpeed * dtFac, steppableBox.Y2 - entity.CollisionBox.Y1 + stepClearance));
                }
                else
                {
                    newYPos = Math.Max(newYPos, pos.Y + stepUpSpeed * dtFac);
                }
            }
        }
        if (newYPos <= pos.Y || !canStepUp(pos, newYPos - pos.Y)) return false;

        pos.Y = newYPos;
        collisionTester.ApplyTerrainCollision(entity, pos, dtFac, ref newPos);
        return true;
    }
    #endregion


    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        disposeRemoteStandingCollider();
    }

}
