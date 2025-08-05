using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

#nullable disable
namespace Vintagestory.GameContent;

// Client-side player physics.
public class EntityBehaviorPlayerPhysics : EntityBehaviorControlledPhysics, IRenderer, IRemotePhysics
{
    private IPlayer player;
    private IServerPlayer serverPlayer;
    private EntityPlayer entityPlayer;    

    // 60/s client-side updates.
    private const float interval = 1 / 60f;
    private float accum = 0;
    private int currentTick;

    public double RenderOrder => 1;

    public int RenderRange => 9999;

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
        }
        else
        {
            EnumHandling handling = EnumHandling.Handled;
            OnReceivedServerPos(true, ref handling);
        }

        entity.PhysicsUpdateWatcher?.Invoke(0, entity.SidedPos.XYZ);
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

    public override void OnReceivedServerPos(bool isTeleport, ref EnumHandling handled)
    {
        //int tickDiff = entity.Attributes.GetInt("tickDiff", 1);
        //HandleRemotePhysics(clientInterval * tickDiff, isTeleport);
    }

    public new void OnReceivedClientPos(int version)
    {
        serverPlayer ??= entityPlayer.Player as IServerPlayer;
        entity.ServerPos.SetFrom(entity.Pos);

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
            nPos.Set(entity.ServerPos);
        }

        var lPos = this.lPos;
        float dtFactor = dt * 60;

        lPos.SetFrom(nPos);
        nPos.Set(entity.ServerPos);
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
        entity.ServerPos.Motion.Set(lPos.Motion);

        collisionTester.NewTick(lPos);

        EntityAgent eagent = entity as EntityAgent;
        if (eagent.MountedOn != null)
        {
            entity.Swimming = false;
            entity.OnGround = false;

            if (capi != null)
            {
                entity.Pos.SetPos(eagent.MountedOn.SeatPosition);
            }

            entity.ServerPos.Motion.X = 0;
            entity.ServerPos.Motion.Y = 0;
            entity.ServerPos.Motion.Z = 0;

            // No-clip detection.
            if (sapi != null)
            {
                collisionTester.ApplyTerrainCollision(entity, lPos, dtFactor, ref newPos, 0, 0);
            }

            return;
        }

        // Set pos for triggering events.
        entity.Pos.SetFrom(entity.ServerPos);

        SetState(lPos, dt);


        EntityControls controls = eagent.Controls;
        if (!controls.NoClip)
        {
            if (sapi != null)
            {
                collisionTester.ApplyTerrainCollision(entity, lPos, dtFactor, ref newPos, 0, 0);
            }

            RemoteMotionAndCollision(lPos, dtFactor);
            ApplyTests(lPos, eagent.Controls, dt, true);
        } else
        {
            var pos = entity.ServerPos;

            pos.X += pos.Motion.X * dt * 60f;
            pos.Y += pos.Motion.Y * dt * 60f;
            pos.Z += pos.Motion.Z * dt * 60f;
            entity.Swimming = false;
            entity.FeetInLiquid = false;
            entity.OnGround = false;
            controls.Gliding = false;
        }
    }


    // Main client physics tick called every frame.
    public override void OnPhysicsTick(float dt)
    {
        SimPhysics(dt, entity.SidedPos);
    }

    public override void OnGameTick(float deltaTime)
    {
        // Player physics is called only client side, but we still need to call Block.OnEntityInside and other usual server-side AfterPhysicsTick things
        if (entity.World is IServerWorldAccessor)
        {
            callOnEntityInside();
            entity.AfterPhysicsTick?.Invoke();
        }

        // note: no need to invoke AfterPhysicsTick on the client, as client-side it will be called from this behavior's OnRenderFrame() method
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
            entity.Swimming = false;
            entity.OnGround = false;

            pos.SetPos(eagent.MountedOn.SeatPosition);

            pos.Motion.X = 0;
            pos.Motion.Y = 0;
            pos.Motion.Z = 0;
            return;
        }

        MotionAndCollision(pos, controls, dt);
        if (!controls.NoClip)
        {
            collisionTester.NewTick(pos);

            if (prevDimension != pos.Dimension)
            {
                prevDimension = pos.Dimension;

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
    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (capi.IsGamePaused) return;

        // Unregister the entity if it isn't the player.
        if (capi.World.Player.Entity != entity)
        {
            smoothStepping = false;
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            return;
        }

        accum += dt;

        if (accum > 0.5)
        {
            accum = 0;
        }

        var mountedEntity = entityPlayer.MountedOn?.Entity;
        IPhysicsTickable tickable = null;
        if (entityPlayer.MountedOn?.MountSupplier.Controller == entityPlayer)
        {
            tickable = mountedEntity?.SidedProperties.Behaviors.Find(b => b is IPhysicsTickable) as IPhysicsTickable;
        }

        while (accum >= interval)
        {
            OnPhysicsTick(interval);
            tickable?.OnPhysicsTick(interval);

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

        double max = 0.75;
        double searchBoxLength = max + (controls.Sprint ? 0.25 : controls.Sneak ? 0.05 : 0.2);

        Vec2d center = new((entityCollisionBox.X1 + entityCollisionBox.X2) / 2, (entityCollisionBox.Z1 + entityCollisionBox.Z2) / 2);
        double searchHeight = Math.Max(entityCollisionBox.Y1 + StepHeight, entityCollisionBox.Y2);
        entityCollisionBox.Translate(pos.X, pos.Y, pos.Z);

        Vec3d walkVec = controls.WalkVector.Clone();
        Vec3d walkVecNormalized = walkVec.Clone().Normalize();

        Cuboidd entitySensorBox;

        double outerX = walkVecNormalized.X * searchBoxLength;
        double outerZ = walkVecNormalized.Z * searchBoxLength;

        entitySensorBox = new Cuboidd
        {
            X1 = Math.Min(0, outerX),
            X2 = Math.Max(0, outerX),

            Z1 = Math.Min(0, outerZ),
            Z2 = Math.Max(0, outerZ),

            Y1 = entity.CollisionBox.Y1 + 0.01 - (!entity.CollidedVertically && !controls.Jump ? 0.05 : 0),

            Y2 = searchHeight
        };

        entitySensorBox.Translate(center.X, 0, center.Y);
        entitySensorBox.Translate(pos.X, pos.Y, pos.Z);

        Vec3d testVec = new();
        Vec2d testMotion = new();

        List<Cuboidd> steppableBoxes = FindSteppableCollisionboxSmooth(entityCollisionBox, entitySensorBox, moveDelta.Y, walkVec);

        if (steppableBoxes != null && steppableBoxes.Count > 0)
        {
            if (TryStepSmooth(controls, pos, testMotion.Set(walkVec.X, walkVec.Z), dtFac, steppableBoxes, entityCollisionBox)) return true;

            Cuboidd entitySensorBoxXAligned = entitySensorBox.Clone();
            if (entitySensorBoxXAligned.Z1 == pos.Z + center.Y)
            {
                entitySensorBoxXAligned.Z2 = entitySensorBoxXAligned.Z1;
            }
            else
            {
                entitySensorBoxXAligned.Z1 = entitySensorBoxXAligned.Z2;
            }

            if (TryStepSmooth(controls, pos, testMotion.Set(walkVec.X, 0), dtFac, FindSteppableCollisionboxSmooth(entityCollisionBox, entitySensorBoxXAligned, moveDelta.Y, testVec.Set(walkVec.X, walkVec.Y, 0)), entityCollisionBox)) return true;

            Cuboidd entitySensorBoxZAligned = entitySensorBox.Clone();
            if (entitySensorBoxZAligned.X1 == pos.X + center.X)
            {
                entitySensorBoxZAligned.X2 = entitySensorBoxZAligned.X1;
            }
            else
            {
                entitySensorBoxZAligned.X1 = entitySensorBoxZAligned.X2;
            }

            if (TryStepSmooth(controls, pos, testMotion.Set(0, walkVec.Z), dtFac, FindSteppableCollisionboxSmooth(entityCollisionBox, entitySensorBoxZAligned, moveDelta.Y, testVec.Set(0, walkVec.Y, walkVec.Z)), entityCollisionBox)) return true;
        }

        return false;
    }



    public bool TryStepSmooth(EntityControls controls, EntityPos pos, Vec2d walkVec, float dtFac, List<Cuboidd> steppableBoxes, Cuboidd entityCollisionBox)
    {
        if (steppableBoxes == null || steppableBoxes.Count == 0) return false;
        double gravityOffset = 0.03;

        Vec2d walkVecOrtho = new Vec2d(walkVec.Y, -walkVec.X).Normalize();

        double maxX = Math.Abs(walkVecOrtho.X * 0.3) + 0.001;
        double minX = -maxX;
        double maxZ = Math.Abs(walkVecOrtho.Y * 0.3) + 0.001;
        double minZ = -maxZ;
        Cuboidf col = new((float)minX, entity.CollisionBox.Y1, (float)minZ, (float)maxX, entity.CollisionBox.Y2, (float)maxZ);

        double newYPos = pos.Y;
        bool foundStep = false;
        foreach (Cuboidd steppableBox in steppableBoxes)
        {
            double heightDiff = steppableBox.Y2 - entityCollisionBox.Y1 + gravityOffset;
            Vec3d stepPos = new(GameMath.Clamp(newPos.X, steppableBox.MinX, steppableBox.MaxX), newPos.Y + heightDiff + pos.DimensionYAdjustment, GameMath.Clamp(newPos.Z, steppableBox.MinZ, steppableBox.MaxZ));

            bool canStep = !collisionTester.IsColliding(entity.World.BlockAccessor, col, stepPos, false);

            if (canStep)
            {
                double elevateFactor = controls.Sprint ? 0.10 : controls.Sneak ? 0.025 : 0.05;
                if (!steppableBox.IntersectsOrTouches(entityCollisionBox))
                {
                    newYPos = Math.Max(newYPos, Math.Min(pos.Y + (elevateFactor * dtFac), steppableBox.Y2 - entity.CollisionBox.Y1 + gravityOffset));
                }
                else
                {
                    newYPos = Math.Max(newYPos, pos.Y + (elevateFactor * dtFac));
                }
                foundStep = true;
            }
        }
        if (foundStep)
        {
            pos.Y = newYPos;
            collisionTester.ApplyTerrainCollision(entity, pos, dtFac, ref newPos);
        }
        return foundStep;
    }
    #endregion


    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        capi?.Event.UnregisterRenderer(this, EnumRenderStage.Before);
    }

    public void Dispose() { }


}
