using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

#nullable disable

public struct PositionSnapshot
{
    public double x;
    public double y;
    public double z;

    public float interval;

    public bool isTeleport;

    public PositionSnapshot(Vec3d pos, float interval, bool isTeleport)
    {
        x = pos.X;
        y = pos.Y;
        z = pos.Z;

        this.interval = interval;
        this.isTeleport = isTeleport;
    }

    public PositionSnapshot(EntityPos pos, float interval, bool isTeleport)
    {
        x = pos.X;
        y = pos.Y;
        z = pos.Z;

        this.interval = interval;
        if (this.interval == 0)
        {
            this.interval = EntityBehaviorInterpolatePosition.interval;
        }
        this.isTeleport = isTeleport;
    }
}

/// <summary>
/// Updates entity position on client based on server updates.
/// <br/>Uses the "interpolateposition" code
/// </summary>
[DocumentAsJson]
public class EntityBehaviorInterpolatePosition : EntityBehavior, IRenderer
{
    public ICoreClientAPI capi;
    public EntityAgent agent;
    public IMountable mountableSupplier;
    public IRemotePhysics physics;
    private PhysicsBehaviorBase entityPhysics;

    public EntityBehaviorInterpolatePosition(Entity entity) : base(entity)
    {
        if (entity.World.Side == EnumAppSide.Server) throw new Exception($"Remove server interpolation behavior from {entity.Code.Path}.");

        capi = entity.Api as ICoreClientAPI;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "interpolateposition");
        agent = entity as EntityAgent;
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        pN = pL = new PositionSnapshot(entity.Pos, interval, false);
        // Wait for a real target instead of queuing another copy of the spawn position.
        wait = 1;

        targetYaw = entity.Pos.Yaw;
        targetPitch = entity.Pos.Pitch;
        targetRoll = entity.Pos.Roll;

        currentYaw = entity.Pos.Yaw;
        currentPitch = entity.Pos.Pitch;
        currentRoll = entity.Pos.Roll;

        if (agent != null)
        {
            targetHeadYaw = entity.Pos.HeadYaw;
            targetHeadPitch = entity.Pos.HeadPitch;
            targetBodyYaw = agent.BodyYawServer;

            currentHeadYaw = entity.Pos.HeadYaw;
            currentHeadPitch = entity.Pos.HeadPitch;
            currentBodyYaw = agent.BodyYawServer;
        }

        foreach (EntityBehavior behavior in entity.SidedProperties.Behaviors)
        {
            if (behavior is IRemotePhysics)
            {
                physics = behavior as IRemotePhysics;
                entityPhysics = physics as PhysicsBehaviorBase;
                break;
            }
        }
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        mountableSupplier = entity.GetInterface<IMountable>();
    }

    private bool wasLocallyControlled;
    private bool resetRemotePhysics;
    private bool awaitingInitialPosition = true;

    public float dtAccum = 0;

    // Will lerp from pL to pN.
    public PositionSnapshot pL;
    public PositionSnapshot pN;

    public Queue<PositionSnapshot> positionQueue = new();

    public float currentYaw;
    public float targetYaw;

    public float currentPitch;
    public float targetPitch;

    public float currentRoll;
    public float targetRoll;

    public float currentHeadYaw;
    public float targetHeadYaw;

    public float currentHeadPitch;
    public float targetHeadPitch;

    public float currentBodyYaw;
    public float targetBodyYaw;

    public void PushQueue(PositionSnapshot snapshot)
    {
        positionQueue.Enqueue(snapshot);
        queueCount++;
    }

    // Interval at what things should be received.
    public const float interval = 1 / 15f;
    public int queueCount;

    public void PopQueue(bool clear)
    {
        dtAccum -= pN.interval;

        if (dtAccum < 0) dtAccum = 0;
        if (dtAccum > 1) dtAccum = 0;

        pL = pN;
        pN = positionQueue.Dequeue();
        queueCount--;

        bool controlledByServer = entity.IsControlledByServer(mountableSupplier);

        // Clear flooded queue.
        if (clear && queueCount > 1) PopQueue(true);

        if (controlledByServer)
        {
            entity.Pos.SetPos(pN.x, pN.y, pN.z);
            physics?.HandleRemotePhysics(Math.Max(pN.interval, interval), pN.isTeleport || resetRemotePhysics);
            resetRemotePhysics = false;
        }
    }
    /// <summary>
    /// Called when the client receives a new position.
    /// Move the positions forward and reset the accumulation.
    /// </summary>
    public override void OnReceivedServerPos(bool isTeleport, ref EnumHandling handled)
    {
        float tickInterval = entity.Attributes.GetInt("tickDiff", 1) * interval;

        PushQueue(new PositionSnapshot(entity.Pos, tickInterval, isTeleport));

        if (isTeleport)
        {
            dtAccum = 0;
            positionQueue.Clear();
            queueCount = 0;

            PushQueue(new PositionSnapshot(entity.Pos, tickInterval, false));
            PushQueue(new PositionSnapshot(entity.Pos, tickInterval, false));

            PopQueue(false);
            PopQueue(false);

            currentYaw = entity.Pos.Yaw;
            currentPitch = entity.Pos.Pitch;
            currentRoll = entity.Pos.Roll;

            if (agent != null)
            {
                currentHeadYaw = entity.Pos.HeadYaw;
                currentHeadPitch = entity.Pos.HeadPitch;
                currentBodyYaw = agent.BodyYawServer;
            }
        }
        else if (awaitingInitialPosition)
        {
            // Start the spawn-to-first-update segment immediately, without a stationary interval.
            dtAccum = 0;
            PopQueue(false);
        }

        if (awaitingInitialPosition)
        {
            awaitingInitialPosition = false;
            wait = 0;
        }

        targetYaw = entity.Pos.Yaw;
        targetPitch = entity.Pos.Pitch;
        targetRoll = entity.Pos.Roll;

        if (agent != null)
        {
            targetHeadYaw = entity.Pos.HeadYaw;
            targetHeadPitch = entity.Pos.HeadPitch;
            targetBodyYaw = agent.BodyYawServer;
        }

        if (queueCount > 20)
        {
            PopQueue(true);
        }
    }

    public int wait = 0;
    public float targetSpeed = 0.6f;


    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        // Local player rotation and head pose are driven by input, even when the position queue is empty.
        if (entity == capi.World.Player.Entity)
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            return;
        }

        if (capi.IsGamePaused) return;

        if (entity.WatchedAttributes.GetBool("clearPosLerp"))
        {
            entity.WatchedAttributes.SetBool("clearPosLerp", false);
            dtAccum = 0;
            positionQueue.Clear();
            queueCount = 0;
        }

        bool locallyControlled = entity.IsControlledByClient(capi.World.Player.Entity);
        if (wasLocallyControlled && !locallyControlled)
        {
            // Remote interpolation must restart from the pose where local control ended.
            positionQueue.Clear();
            queueCount = 0;
            pL = pN = new PositionSnapshot(entity.Pos, interval, false);
            dtAccum = 0;
            wait = 1;
            currentYaw = targetYaw = entity.Pos.Yaw;
            currentPitch = targetPitch = entity.Pos.Pitch;
            currentRoll = targetRoll = entity.Pos.Roll;
            resetRemotePhysics = true;
        }
        wasLocallyControlled = locallyControlled;

        if (queueCount < wait)
        {
            if (mountableSupplier == null)
            {
                entity.Pos.Yaw = LerpRotation(ref currentYaw, targetYaw, dt);
                entity.Pos.Pitch = LerpRotation(ref currentPitch, targetPitch, dt);
                entity.Pos.Roll = LerpRotation(ref currentRoll, targetRoll, dt);

                if (agent != null)
                {
                    entity.Pos.HeadYaw = LerpRotation(ref currentHeadYaw, targetHeadYaw, dt);
                    entity.Pos.HeadPitch = LerpRotation(ref currentHeadPitch, targetHeadPitch, dt);
                    agent.BodyYaw = LerpRotation(ref currentBodyYaw, targetBodyYaw, dt);
                }

                entityPhysics?.SetInterpolatedRemotePose(pN.x, pN.y, pN.z, entity.Pos.Yaw);
            }

            return;
        }

        dtAccum += dt * targetSpeed;

        while (dtAccum > pN.interval)
        {
            if (queueCount > 0)
            {
                PopQueue(false);
                wait = 0;
            }
            else
            {
                wait = 1;

                break;
            }
        }

        float speed = (queueCount * 0.2f) + 0.8f;
        targetSpeed = GameMath.Lerp(targetSpeed, speed, dt * 4);

        // Don't set position if the player is controlling the mount.
        if (mountableSupplier != null)
        {
            foreach (IMountableSeat seat in mountableSupplier.Seats)
            {
                // Set position of other entities.
                if (seat.Passenger != capi.World.Player.Entity)
                {
                    seat.Passenger?.Pos.SetFrom(seat.SeatPosition);
                }
                else
                {
                    if (mountableSupplier.Controller == capi.World.Player.Entity)
                    {
                        currentYaw = entity.Pos.Yaw;
                        currentPitch = entity.Pos.Pitch;
                        currentRoll = entity.Pos.Roll;
                        return;
                    }
                    else entity.OnGround = entity.Pos.Motion.Y == 0;    // Not an ideal test but good enough, needed mainly for the "swivel" system when rendering turning. Another player's mount has no physics ticks client-side, so this is the only place where we can set OnGround correctly. (One day we might need to set other states such as swimming here also?)
                }
            }
        }

        entity.Pos.Yaw = LerpRotation(ref currentYaw, targetYaw, dt);
        entity.Pos.Pitch = LerpRotation(ref currentPitch, targetPitch, dt);
        entity.Pos.Roll = LerpRotation(ref currentRoll, targetRoll, dt);

        if (agent != null)
        {
            entity.Pos.HeadYaw = LerpRotation(ref currentHeadYaw, targetHeadYaw, dt);
            entity.Pos.HeadPitch = LerpRotation(ref currentHeadPitch, targetHeadPitch, dt);
            agent.BodyYaw = LerpRotation(ref currentBodyYaw, targetBodyYaw, dt);
        }

        // Only set position if not mounted.
        if (agent == null || agent.MountedOn == null)
        {
            float delta = dtAccum / pN.interval;
            if (wait != 0) delta = 1;
            entity.Pos.X = GameMath.Lerp(pL.x, pN.x, delta);
            entity.Pos.Y = GameMath.Lerp(pL.y, pN.y, delta);
            entity.Pos.Z = GameMath.Lerp(pL.z, pN.z, delta);
            entityPhysics?.SetInterpolatedRemotePose(entity.Pos.X, entity.Pos.Y, entity.Pos.Z, entity.Pos.Yaw);
        }
    }

    // I don't like doing it like this but AI turns instantly so using the actual lerp is bad.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float LerpRotation(ref float current, float target, float dt)
    {
        float pDiff = Math.Abs(GameMath.AngleRadDistance(current, target)) * dt / 0.1f;
        int signY = Math.Sign(pDiff);
        current += 0.6f * Math.Clamp(GameMath.AngleRadDistance(current, target), -signY * pDiff, signY * pDiff);
        current %= GameMath.TWOPI;

        return current;
    }

    public override string PropertyName()
    {
        return "entityinterpolation";
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
    }

    public void Dispose()
    {

    }

    public double RenderOrder => 0;
    public int RenderRange => 9999;
}
