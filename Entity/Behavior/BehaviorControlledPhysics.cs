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

public class EntityBehaviorControlledPhysics : PhysicsBehaviorBase, IPhysicsTickable, IRemotePhysics
{
    protected const double collisionboxReductionForInsideBlocksCheck = 0.009;
    public Entity Entity { get { return entity; }}
    protected bool smoothStepping = false;
    protected readonly List<PModule> physicsModules = new();
    protected readonly List<PModule> customModules = new();
    protected Vec3d newPos = new();
    protected readonly Vec3d prevPos = new();
    protected readonly BlockPos tmpPos = new();
    protected readonly Cuboidd entityBox = new();
    protected readonly List<FastVec3i> traversed = new(4);
    protected readonly List<Block> traversedBlocks = new(4);
    protected readonly IComparer<FastVec3i> fastVec3IComparer = new FastVec3iComparer();
    protected readonly Vec3d moveDelta = new();
    protected double prevYMotion;
    protected bool onGroundBefore;
    protected bool feetInLiquidBefore;
    protected bool swimmingBefore;
    protected float knockBackCounter = 0;
    protected Cuboidf sneakTestCollisionbox = new();
    protected readonly Cuboidd steppingCollisionBox = new();
    protected readonly Vec3d steppingTestVec = new();
    protected readonly Vec3d steppingTestMotion = new();

    /// <summary>
    /// For adjusting hitbox to dying enemies.
    /// </summary>
    public Matrixf tmpModelMat = new();
    public float StepHeight = 0.6f;
    public bool Ticking { get; set; }

    public float stepUpSpeed = 0.07f;
    public float climbUpSpeed = 0.07f;
    public float climbDownSpeed = 0.035f;

    public override bool ThreadSafe { get { return true; } }     // It's threadsafe for a subtle reason: in OnGameTick(), the only entities for which callOnEntityInside() will be called, are exactly those entities physics-ticked on the main thread, i.e. players and player-controlled mounts

    public void SetState(EntityPos pos, float dt)
    {
        float dtFactor = dt * 60;
        var entity = this.entity;

        prevPos.Set(pos);
        prevYMotion = pos.Motion.Y;
        onGroundBefore = entity.OnGround;
        feetInLiquidBefore = entity.FeetInLiquid;
        swimmingBefore = entity.Swimming;

        traversed.Clear();
        traversedBlocks.Clear();
        if (entity.AdjustCollisionBoxToAnimation)
        {
            AdjustCollisionBoxToAnimation(dtFactor);
        }
    }

    public EntityBehaviorControlledPhysics(Entity entity) : base(entity) { }

    public virtual void SetModules()
    {
        physicsModules.Add(new PModuleWind());
        physicsModules.Add(new PModuleOnGround());
        physicsModules.Add(new PModuleInLiquid());
        physicsModules.Add(new PModuleInAir());
        physicsModules.Add(new PModuleGravity());
        physicsModules.Add(new PModuleMotionDrag());
        physicsModules.Add(new PModuleKnockback());
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        Init();
        SetProperties(properties, attributes);

        if (entity.Api is ICoreServerAPI esapi)
        {
            esapi.Server.AddPhysicsTickable(this);
        }

        entity.PhysicsUpdateWatcher?.Invoke(0, entity.SidedPos.XYZ);

        // This is for entity shape renderer. Somewhere else should determine when this is set since it can be on both sides now.
        if (entity.Api.Side == EnumAppSide.Client)
        {
            EnumHandling handling = EnumHandling.Handled;
            OnReceivedServerPos(true, ref handling);

            entity.Attributes.RegisterModifiedListener("dmgkb", () =>
            {
                if (entity.Attributes.GetInt("dmgkb") == 1)
                {
                    knockBackCounter = 2;
                }
            });
        }
    }


    public override void AfterInitialized(bool onFirstSpawn)
    {
        base.AfterInitialized(onFirstSpawn);
    }

    public override void OnGameTick(float deltaTime)
    {
        if (entity.World is IServerWorldAccessor)
        {
            // Player physics is called only client side, but for their mounts we still need to call Block.OnEntityInside and other usual server-side AfterPhysicsTick things
            if (mountableSupplier?.Controller is EntityPlayer p && p.Alive)
        {
            callOnEntityInside();
                entity.AfterPhysicsTick?.Invoke();
        }

            return;
        }

        // Client-side, we need to invoke any AfterPhysicsTick here (i.e. to update positions in BehaviorRepulseAgents) as it is called no place else on the client, for non-player entities
        entity.AfterPhysicsTick?.Invoke();
    }

    public void SetProperties(EntityProperties properties, JsonObject attributes)
    {
        StepHeight = attributes["stepHeight"].AsFloat(0.6f);
        stepUpSpeed = attributes["stepUpSpeed"].AsFloat(0.07f);
        climbUpSpeed = attributes["climbUpSpeed"].AsFloat(0.07f);
        climbDownSpeed = attributes["climbDownSpeed"].AsFloat(0.035f);
        sneakTestCollisionbox = entity.CollisionBox.Clone().OmniNotDownGrowBy(-0.1f);
        sneakTestCollisionbox.Y2 /= 2;

        SetModules();

        JsonObject physics = properties?.Attributes?["physics"];
        for (int i = 0; i < physicsModules.Count; i++)
        {
            physicsModules[i].Initialize(physics, entity);
        }
    }

    public override void OnReceivedServerPos(bool isTeleport, ref EnumHandling handled) { }

    public void OnReceivedClientPos(int version)
    {
        if (version > previousVersion)
        {
            previousVersion = version;
            HandleRemotePhysics(clientInterval, true);
            return;
        }

        HandleRemotePhysics(clientInterval, false);
    }

    public void HandleRemotePhysics(float dt, bool isTeleport)
    {
        if (nPos == null)
        {
            nPos = new();
            nPos.Set(entity.ServerPos);
        }

        float dtFactor = dt * 60;

        lPos.SetFrom(nPos);
        nPos.Set(entity.ServerPos);

        if (isTeleport) lPos.SetFrom(nPos);
        lPos.Dimension = entity.Pos.Dimension;
        tmpPos.dimension = lPos.Dimension;

        lPos.Motion.X = (nPos.X - lPos.X) / dtFactor;
        lPos.Motion.Y = (nPos.Y - lPos.Y) / dtFactor;
        lPos.Motion.Z = (nPos.Z - lPos.Z) / dtFactor;

        if (lPos.Motion.Length() > 20) lPos.Motion.Set(0, 0, 0);

        // Set client motion.
        entity.Pos.Motion.Set(lPos.Motion);
        entity.ServerPos.Motion.Set(lPos.Motion);

        collisionTester.NewTick(lPos);

        EntityAgent agent = entity as EntityAgent;
        if (agent?.MountedOn != null)
        {
            entity.Swimming = false;
            entity.OnGround = false;

            if (capi != null)
            {
                entity.Pos.SetPos(agent.MountedOn.SeatPosition);
            }

            entity.ServerPos.Motion.X = 0;
            entity.ServerPos.Motion.Y = 0;
            entity.ServerPos.Motion.Z = 0;
            return;
        }

        // Set pos for triggering events (interpolation overrides this).
        entity.Pos.SetFrom(entity.ServerPos);

        SetState(lPos, dt);
        RemoteMotionAndCollision(lPos, dtFactor);
        ApplyTests(lPos, ((EntityAgent)entity).Controls, dt, true);

        // Knockback is only removed on the server in the knockback module. It needs to be set on the client so entities don't remain tilted.
        // Should always set it to 1 when taking damage instead of when it's 0 in the entity class so the timer can always get updated.
        // Entity should tilt back to normal state if it's being knocked back too.
        if (knockBackCounter > 0)
        {
            knockBackCounter -= dt;
        }
        else
        {
            knockBackCounter = 0;
            entity.Attributes.SetInt("dmgkb", 0);
        }
    }

    public void RemoteMotionAndCollision(EntityPos pos, float dtFactor)
    {
        double gravityStrength = (1 / 60f * dtFactor) + Math.Max(0, -0.015f * pos.Motion.Y * dtFactor);
        pos.Motion.Y -= gravityStrength;
        collisionTester.ApplyTerrainCollision(entity, pos, dtFactor, ref newPos, 0, 0);
        bool falling = lPos.Motion.Y < 0;
        entity.OnGround = entity.CollidedVertically && falling;
        pos.Motion.Y += gravityStrength;
        pos.SetPos(nPos);
    }

    public void MotionAndCollision(EntityPos pos, EntityControls controls, float dt)
    {
        var entity = this.entity;
        foreach (PModule physicsModule in physicsModules)
        {
            if (physicsModule.Applicable(entity, pos, controls))
            {
                physicsModule.DoApply(dt, entity, pos, controls);
            }
        }

        foreach (PModule physicsModule in customModules)
        {
            if (physicsModule.Applicable(entity, pos, controls))
            {
                physicsModule.DoApply(dt, entity, pos, controls);
            }
        }
    }

    public void ApplyTests(EntityPos pos, EntityControls controls, float dt, bool remote)
    {
        var entity = this.entity;
        var entityProperties = entity.Properties;
        IBlockAccessor blockAccessor = entity.World.BlockAccessor;
        float dtFactor = dt * 60;
        BlockPos tmpPos = this.tmpPos;
        var entityBox = this.entityBox;
        Vec3d motion = pos.Motion;
        Vec3d newPos = this.newPos;

        controls.IsClimbing = false;
        entity.ClimbingOnFace = null;
        entity.ClimbingIntoFace = null;
        if (entityProperties.CanClimb == true)    // For example it will be true for:  players, drifters, bowtorn, shivers, locusts, bell, bear, trader, villager
        {                                         // All these very costly checks are essentially looking for ladders!!  (except in the case of locust and mechhelper, who can climb anywhere)
            bool canClimbAnywhere = entityProperties.CanClimbAnywhere && entity.Alive;
            int searchBlockLayer = canClimbAnywhere ? BlockLayersAccess.Default : BlockLayersAccess.Solid;

            int height = (int)Math.Ceiling(entity.CollisionBox.Y2);
            entityBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);
            tmpPos.Set((int)pos.X, 0, (int)pos.Z);
            for (int dy = 0; dy < height; dy++)
            {
                tmpPos.Y = (int)pos.Y + dy;
                Block inBlock = blockAccessor.GetBlock(tmpPos, searchBlockLayer);
                if (!inBlock.IsClimbable(tmpPos) && !canClimbAnywhere) continue;
                Cuboidf[] collisionBoxes = inBlock.GetCollisionBoxes(blockAccessor, tmpPos);
                if (collisionBoxes == null) continue;
                for (int i = 0; i < collisionBoxes.Length; i++)
                {
                    double distance = entityBox.ShortestDistanceFrom(collisionBoxes[i], tmpPos);
                    controls.IsClimbing |= distance < entityProperties.ClimbTouchDistance;

                    if (controls.IsClimbing)
                    {
                        entity.ClimbingOnFace = null;
                        break;
                    }
                }
            }

            if (canClimbAnywhere && controls.WalkVector.LengthSq() > 0.00001)
            {
                BlockFacing walkIntoFace = BlockFacing.FromVector(controls.WalkVector);
                if (walkIntoFace != null)
                {
                    tmpPos.Set((int)pos.X + walkIntoFace.Normali.X, (int)pos.Y + walkIntoFace.Normali.Y, (int)pos.Z + walkIntoFace.Normali.Z);
                    Block inBlock = blockAccessor.GetBlock(tmpPos, BlockLayersAccess.Default);

                    Cuboidf[] collisionBoxes = inBlock.GetCollisionBoxes(blockAccessor, tmpPos);
                    entity.ClimbingIntoFace = (collisionBoxes != null && collisionBoxes.Length != 0) ? walkIntoFace : null;
                }
            }

            if (!controls.IsClimbing)
            {
                float touchDistance = entityProperties.ClimbTouchDistance;
                int baseY = (int)pos.Y;
                for (int i = 0; i < 4; i++)
                {
                    tmpPos.IterateHorizontalOffsets(i);
                for (int dy = 0; dy < height; dy++)
                {
                        tmpPos.Y = baseY + dy;
                        Block inBlock = blockAccessor.GetBlock(tmpPos, searchBlockLayer);        // This is fairly costly, typically 8 GetBlock calls for each climbing entity
                        if (!inBlock.IsClimbable(tmpPos) && !canClimbAnywhere) continue;

                    Cuboidf[] collisionBoxes = inBlock.GetCollisionBoxes(blockAccessor, tmpPos);
                    if (collisionBoxes == null) continue;

                    for (int j = 0; j < collisionBoxes.Length; j++)
                    {
                        double distance = entityBox.ShortestDistanceFrom(collisionBoxes[j], tmpPos);

                            if (distance < touchDistance)
                        {
                                controls.IsClimbing = true;
                                entity.ClimbingOnFace = BlockFacing.HORIZONTALS[i];
                            entity.ClimbingOnCollBox = collisionBoxes[j];
                                goto DoneClimbing;      // break out of all loops
                        }
                    }
                }
            }
            DoneClimbing:;
            }
        }

        if (!remote)
        {
            if (controls.IsClimbing)
            {
                if (controls.WalkVector.Y == 0)
                {
                    motion.Y = controls.Sneak ? Math.Max(-climbUpSpeed, motion.Y - climbUpSpeed) : motion.Y;
                    if (controls.Jump) motion.Y = climbDownSpeed * dt * 60f;
                }
            }

            double nextX = (motion.X * dtFactor) + pos.X;
            double nextY = (motion.Y * dtFactor) + pos.Y;
            double nextZ = (motion.Z * dtFactor) + pos.Z;

            moveDelta.Set(motion.X * dtFactor, prevYMotion * dtFactor, motion.Z * dtFactor);

            collisionTester.ApplyTerrainCollision(entity, pos, dtFactor, ref newPos, 0, CollisionYExtra);

            if (!entityProperties.CanClimbAnywhere)
            {
                controls.IsStepping = HandleSteppingOnBlocks(pos, moveDelta, dtFactor, controls);
            }

            HandleSneaking(pos, controls, dt);

            int x = (int)pos.X;
            int y = (int)pos.Y;
            int z = (int)pos.Z;

            if (entity.CollidedHorizontally && !controls.IsClimbing && !controls.IsStepping && entityProperties.Habitat != EnumHabitat.Underwater)
            {
                if (blockAccessor.GetBlockRaw(x, (int)(pos.InternalY + 0.5), z, BlockLayersAccess.Fluid).LiquidLevel >= 7 || blockAccessor.GetBlockRaw(x, (int)pos.InternalY, z, BlockLayersAccess.Fluid).LiquidLevel >= 7 || blockAccessor.GetBlockRaw(x, (int)(pos.InternalY - 0.05), z, BlockLayersAccess.Fluid).LiquidLevel >= 7)
                {
                    // Climb out of pool sides
                    motion.Y += 0.2 * dt;
                    controls.IsStepping = true;
                }
                else
                {
                    double absX = Math.Abs(motion.X);
                    double absZ = Math.Abs(motion.Z);
                    if (absX > absZ)
                    {
                        if (absZ < 0.001) motion.Z += motion.Z < 0 ? -0.0025 : 0.0025;
                    }
                    else
                    {
                        if (absX < 0.001) motion.X += motion.X < 0 ? -0.0025 : 0.0025;
                    }
                }
            }

            if (blockAccessor.IsNotTraversable((int)nextX, y, z, pos.Dimension)) newPos.X = pos.X;
            if (blockAccessor.IsNotTraversable(x, (int)nextY, z, pos.Dimension)) newPos.Y = pos.Y;
            if (blockAccessor.IsNotTraversable(x, y, (int)nextZ, pos.Dimension)) newPos.Z = pos.Z;

            pos.SetPos(newPos);

            if ((nextX < newPos.X && motion.X < 0) || (nextX > newPos.X && motion.X > 0)) motion.X = 0;
            if ((nextY < newPos.Y && motion.Y < 0) || (nextY > newPos.Y && motion.Y > 0)) motion.Y = 0;
            if ((nextZ < newPos.Z && motion.Z < 0) || (nextZ > newPos.Z && motion.Z > 0)) motion.Z = 0;
        }

        bool falling = prevYMotion <= 0;
        entity.OnGround = entity.CollidedVertically && falling;

        float offX = entity.CollisionBox.X2 - entity.OriginCollisionBox.X2;
        float offZ = entity.CollisionBox.Z2 - entity.OriginCollisionBox.Z2;

        int posX = (int)(pos.X + offX);
        int posY = (int)pos.InternalY;
        int posZ = (int)(pos.Z + offZ);
        int swimmingY = (int)(pos.InternalY + entity.SwimmingOffsetY);

        Block blockFluid = blockAccessor.GetBlockRaw(posX, posY, posZ, BlockLayersAccess.Fluid);
        Block middleWOIBlock = (swimmingY == posY) ? blockFluid : blockAccessor.GetBlockRaw(posX, swimmingY, posZ, BlockLayersAccess.Fluid);
        entity.Swimming = middleWOIBlock.IsLiquid();

        entity.OnGround = (entity.CollidedVertically && falling && !controls.IsClimbing) || controls.IsStepping;

        if (blockFluid.IsLiquid())
        {
            Block aboveBlock = blockAccessor.GetBlockRaw(posX, posY + 1, posZ, BlockLayersAccess.Fluid);
            entity.FeetInLiquid = (blockFluid.LiquidLevel + (aboveBlock.LiquidLevel > 0 ? 1 : 0)) / 8f >= pos.Y - (int)pos.Y;
        entity.InLava = blockFluid.LiquidCode == "lava";

            if (!feetInLiquidBefore && entity.FeetInLiquid) entity.OnCollideWithLiquid();
        }
        else
        {
            entity.FeetInLiquid = false;
            entity.InLava = false;
        }

        if (!onGroundBefore && entity.OnGround)
        {
            entity.OnFallToGround(prevYMotion);
        }

        if ((swimmingBefore || feetInLiquidBefore) && (!entity.Swimming && !entity.FeetInLiquid))
        {
            entity.OnExitedLiquid();
        }

        if (!falling || entity.OnGround || controls.IsClimbing) entity.PositionBeforeFalling.Set(pos);

        Cuboidd testedEntityBox = collisionTester.entityBox;
        int xMax = (int)(testedEntityBox.X2 - collisionboxReductionForInsideBlocksCheck);
        int yMax = (int)(testedEntityBox.Y2 - collisionboxReductionForInsideBlocksCheck);
        int zMax = (int)(testedEntityBox.Z2 - collisionboxReductionForInsideBlocksCheck);
        int xMin = (int)(testedEntityBox.X1 + collisionboxReductionForInsideBlocksCheck);
        int zMin = (int)(testedEntityBox.Z1 + collisionboxReductionForInsideBlocksCheck);
        for (int y = (int)(testedEntityBox.Y1 + collisionboxReductionForInsideBlocksCheck); y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                for (int z = zMin; z <= zMax; z++)
                {
                    tmpPos.Set(x, y, z);
                    Block block = blockAccessor.GetBlock(tmpPos);
                    if (block.Id == 0) continue;  // Don't store air blocks as traversed, as we know they do nothing in InsideBlock checks in AfterPhysicsTick()
                    FastVec3i posTraversed = new(x, y, z);
                    int index = traversed.BinarySearch(posTraversed, fastVec3IComparer);
                    if (index < 0) index = ~index;
                    traversed.Insert(index, posTraversed);
                    traversedBlocks.Insert(index, block);
                }
            }
        }

        entity.PhysicsUpdateWatcher?.Invoke(0, prevPos);
    }

    public virtual void OnPhysicsTick(float dt)
    {
        var entity = this.entity;
        if (entity.State != EnumEntityState.Active) return;

        EntityPos pos = entity.SidedPos;
        collisionTester.AssignToEntity(this, pos.Dimension);

        EntityControls controls = ((EntityAgent)entity).Controls;
        EntityAgent agent = entity as EntityAgent;
        if (agent?.MountedOn != null)
        {
            AdjustMountedPositionFor(agent);
            return;
        }

        SetState(pos, dt);
        MotionAndCollision(pos, controls, dt);
        ApplyTests(pos, controls, dt, false);

        // For falling
        if (entity.World.Side == EnumAppSide.Server)
        {
            entity.Pos.SetFrom(entity.ServerPos);
        }

        // We make the same adjustment to all the passengers' positions when ticking the mount, because we might tick the SeatPosition later than ticking the passenger entity
        if (mountableSupplier is IMountable mount)
        {
            foreach (var seat in mount.Seats) if (seat?.Passenger is EntityAgent ea && ea.MountedOn != null) AdjustMountedPositionFor(ea);
        }
    }

    private void AdjustMountedPositionFor(EntityAgent entity)
    {
        entity.Swimming = false;
        entity.OnGround = false;
        var pos = entity.SidedPos;

        if (!(entity is EntityPlayer))
        {
            pos.SetFrom(entity.MountedOn.SeatPosition);
        }
        else
        {
            pos.SetPos(entity.MountedOn.SeatPosition);
        }

        pos.Motion.X = 0;
        pos.Motion.Y = 0;
        pos.Motion.Z = 0;
    }

    public virtual void AfterPhysicsTick(float dt)
    {
        var entity = this.entity;
        if (entity.State != EnumEntityState.Active) return;

        if (mountableSupplier != null && capi == null && mountableSupplier.IsBeingControlled()) return;

        // Call OnEntityInside events.
        var tmpPos = this.tmpPos;
        var traversedBlocks = this.traversedBlocks;
        var traversed = this.traversed;
        for (int i = 0; i < traversedBlocks.Count; i++)
        {
            tmpPos.Set(traversed[i]);
            traversedBlocks[i].OnEntityInside(entity.World, entity, tmpPos);
        }

        entity.AfterPhysicsTick?.Invoke();
    }

    public void HandleSneaking(EntityPos pos, EntityControls controls, float dt)
    {
        if (!controls.Sneak || !entity.OnGround || pos.Motion.Y > 0) return;

        // Sneak to prevent falling off blocks.
        Vec3d testPosition = new();
        testPosition.Set(pos.X, pos.InternalY - (GlobalConstants.GravityPerSecond * dt), pos.Z);

        // Only apply this if the entity is on the ground in the first place.
        if (!collisionTester.IsColliding(entity.World.BlockAccessor, sneakTestCollisionbox, testPosition)) return;

        tmpPos.Set((int)pos.X, (int)pos.Y - 1, (int)pos.Z);
        Block belowBlock = entity.World.BlockAccessor.GetBlock(tmpPos);

        // Test for X.
        testPosition.Set(newPos.X, newPos.Y - (GlobalConstants.GravityPerSecond * dt) + pos.DimensionYAdjustment, pos.Z);
        if (!collisionTester.IsColliding(entity.World.BlockAccessor, sneakTestCollisionbox, testPosition))
        {
            if (belowBlock.IsClimbable(tmpPos))
            {
                newPos.X += (pos.X - newPos.X) / 10;
            }
            else
            {
                newPos.X = pos.X;
            }
        }

        // Test for Z.
        testPosition.Set(pos.X, newPos.Y - (GlobalConstants.GravityPerSecond * dt) + pos.DimensionYAdjustment, newPos.Z);
        if (!collisionTester.IsColliding(entity.World.BlockAccessor, sneakTestCollisionbox, testPosition))
        {
            if (belowBlock.IsClimbable(tmpPos))
            {
                newPos.Z += (pos.Z - newPos.Z) / 10;
            }
            else
            {
                newPos.Z = pos.Z;
            }
        }
    }

    protected virtual bool HandleSteppingOnBlocks(EntityPos pos, Vec3d moveDelta, float dtFac, EntityControls controls)
    {
        if (controls.WalkVector.X == 0 && controls.WalkVector.Z == 0) return false;

        if ((!entity.OnGround && !entity.Swimming) || entity.Properties.Habitat == EnumHabitat.Underwater) return false;

        steppingCollisionBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);
        steppingCollisionBox.Y2 = Math.Max(steppingCollisionBox.Y1 + StepHeight, steppingCollisionBox.Y2);

        Vec3d walkVec = controls.WalkVector;
        Cuboidd steppableBox = FindSteppableCollisionBox(steppingCollisionBox, moveDelta.Y, walkVec);

        if (steppableBox != null)
        {
            Vec3d testMotion = steppingTestMotion;
            testMotion.Set(moveDelta.X, moveDelta.Y, moveDelta.Z);
            if (TryStep(pos, testMotion, dtFac, steppableBox, steppingCollisionBox)) return true;

            Vec3d testVec = steppingTestVec;
            testMotion.Z = 0;
            if (TryStep(pos, testMotion, dtFac, FindSteppableCollisionBox(steppingCollisionBox, moveDelta.Y, testVec.Set(walkVec.X, walkVec.Y, 0)), steppingCollisionBox)) return true;

            testMotion.Set(0, moveDelta.Y, moveDelta.Z);
            if (TryStep(pos, testMotion, dtFac, FindSteppableCollisionBox(steppingCollisionBox, moveDelta.Y, testVec.Set(0, walkVec.Y, walkVec.Z)), steppingCollisionBox)) return true;

            return false;
        }

        return false;
    }



    public bool TryStep(EntityPos pos, Vec3d moveDelta, float dtFac, Cuboidd steppableBox, Cuboidd entityCollisionBox)
    {
        if (steppableBox == null) return false;

        double heightDiff = steppableBox.Y2 - entityCollisionBox.Y1 + (0.01 * 3f);
        Vec3d stepPos = newPos.OffsetCopy(moveDelta.X, heightDiff, moveDelta.Z);
        bool canStep = !collisionTester.IsColliding(entity.World.BlockAccessor, entity.CollisionBox, stepPos, false);
        
        if (canStep)
        {
            pos.Y += stepUpSpeed * dtFac;
            collisionTester.ApplyTerrainCollision(entity, pos, dtFac, ref newPos);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get all blocks colliding with entityBoxRel
    /// </summary>
    /// <param name="blockAccessor"></param>
    /// <param name="entityBoxRel"></param>
    /// <param name="pos"></param>
    /// <param name="blocks">The found blocks</param>
    /// <param name="tmpPos">We have to supply the tmpPos because this is a static method. The tmpPos should be previously set to the correct dimension by the calling code</param>
    /// <param name="alsoCheckTouch"></param>
    /// <returns>If any blocks have been found</returns>
    public static bool GetCollidingCollisionBox(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, out CachedCuboidList blocks, BlockPos tmpPos, bool alsoCheckTouch = true)
    {
        blocks = new CachedCuboidList();
        Vec3d blockPosVec = new();
        Cuboidd entityBox = entityBoxRel.ToDouble().Translate(pos);

        int minX = (int)(entityBoxRel.MinX + pos.X);
        int minY = (int)(entityBoxRel.MinY + pos.Y - 1); // -1 for the extra high collision box of fences.
        int minZ = (int)(entityBoxRel.MinZ + pos.Z);
        int maxX = (int)Math.Ceiling(entityBoxRel.MaxX + pos.X);
        int maxY = (int)Math.Ceiling(entityBoxRel.MaxY + pos.Y);
        int maxZ = (int)Math.Ceiling(entityBoxRel.MaxZ + pos.Z);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    tmpPos.Set(x, y, z);
                    Block block = blockAccessor.GetBlock(tmpPos);
                    blockPosVec.Set(x, y, z);

                    Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, tmpPos);
                    if (collisionBoxes == null) continue;

                    for (int i = 0; i < collisionBoxes.Length; i++)
                    {
                        Cuboidf collBox = collisionBoxes[i];
                        if (collBox == null) continue;

                        bool colliding = alsoCheckTouch ? entityBox.IntersectsOrTouches(collBox, blockPosVec) : entityBox.Intersects(collBox, blockPosVec);

                        if (colliding) blocks.Add(collBox, x, tmpPos.InternalY, z, block);
                    }
                }
            }
        }

        return blocks.Count > 0;
    }

    public Cuboidd FindSteppableCollisionBox(Cuboidd entityCollisionBox, double motionY, Vec3d walkVector)
    {
        Cuboidd steppableBox = null;

        CachedCuboidListFaster blocks = collisionTester.CollisionBoxList;
        int maxCount = blocks.Count;
        BlockPos pos = new BlockPos(entity.ServerPos.Dimension);
        for (int i = 0; i < maxCount; i++)
        {
            Block block = blocks.blocks[i];

            if (block.CollisionBoxes != null && !block.CanStep)
            {
                // Blocks which are low relative to this entity (e.g. small troughs are low for the player) can still be stepped on
                if (entity.CollisionBox.Height < 5 * block.CollisionBoxes[0].Height) continue;
            }

            pos.Set(blocks.positions[i]);
            if (!block.SideIsSolid(pos, BlockFacing.indexUP))    // If we are a non-solid block, check whether the block below is non-steppable, for example lanterns on top of fences
            {
                pos.Down();    // Avoid creating a new BlockPos object
                Block blockBelow = entity.World.BlockAccessor.GetMostSolidBlock(pos);
                pos.Up();
                if (blockBelow.CollisionBoxes != null && !blockBelow.CanStep)
                {
                    if (entity.CollisionBox.Height < 5 * blockBelow.CollisionBoxes[0].Height) continue;
                }
            }

            Cuboidd collisionBox = blocks.cuboids[i];
            EnumIntersect intersect = CollisionTester.AabbIntersect(collisionBox, entityCollisionBox, walkVector);
            if (intersect == EnumIntersect.NoIntersect) continue;

            // Already stuck somewhere? Can't step stairs
            // Would get stuck vertically if I go up? Can't step up either
            if ((intersect == EnumIntersect.Stuck && !block.AllowStepWhenStuck) || (intersect == EnumIntersect.IntersectY && motionY > 0))
            {
                return null;
            }

            double heightDiff = collisionBox.Y2 - entityCollisionBox.Y1;

            if (heightDiff <= 0) continue;
            if (heightDiff <= StepHeight && (steppableBox == null || steppableBox.Y2 < collisionBox.Y2))
            {
                steppableBox = collisionBox;
            }
        }

        return steppableBox;
    }

    public List<Cuboidd> FindSteppableCollisionboxSmooth(Cuboidd entityCollisionBox, Cuboidd entitySensorBox, double motionY, Vec3d walkVector)
    {
        List<Cuboidd> steppableBoxes = new();
        GetCollidingCollisionBox(entity.World.BlockAccessor, entitySensorBox.ToFloat(), new Vec3d(), out var blocks, tmpPos, true);

        for (int i = 0; i < blocks.Count; i++)
        {
            Cuboidd collisionbox = blocks.cuboids[i];
            Block block = blocks.blocks[i];

            if (!block.CanStep && block.CollisionBoxes != null)
            {
                if (entity.CollisionBox.Height < 5 * block.CollisionBoxes[0].Height) continue;
            }

            BlockPos pos = blocks.positions[i];
            if (!block.SideIsSolid(pos, BlockFacing.indexUP))    // If we are a non-solid block, check whether the block below is non-steppable, for example lanterns on top of fences
            {
                pos.Down();    // Avoid creating a new BlockPos object
                Block blockBelow = entity.World.BlockAccessor.GetMostSolidBlock(pos);
                pos.Up();
                if (!blockBelow.CanStep && blockBelow.CollisionBoxes != null)
                {
                    if (entity.CollisionBox.Height < 5 * blockBelow.CollisionBoxes[0].Height) continue;
                }
            }

            EnumIntersect intersect = CollisionTester.AabbIntersect(collisionbox, entityCollisionBox, walkVector);

            if ((intersect == EnumIntersect.Stuck && !block.AllowStepWhenStuck) || (intersect == EnumIntersect.IntersectY && motionY > 0))
            {
                return null;
            }

            double heightDiff = collisionbox.Y2 - entityCollisionBox.Y1;

            if (heightDiff <= (entity.CollidedVertically ? 0 : -0.05)) continue;
            if (heightDiff <= StepHeight)
            {
                steppableBoxes.Add(collisionbox);
            }
        }

        return steppableBoxes;
    }

    public void AdjustCollisionBoxToAnimation(float dtFac)
    {
        AttachmentPointAndPose apap = entity.AnimManager.Animator?.GetAttachmentPointPose("Center");

        if (apap == null) return;

        float[] hitboxOff = new float[4] { 0, 0, 0, 1 };
        AttachmentPoint ap = apap.AttachPoint;

        CompositeShape shape = entity.Properties.Client.Shape;
        float rotX = shape != null ? shape.rotateX : 0;
        float rotY = shape != null ? shape.rotateY : 0;
        float rotZ = shape != null ? shape.rotateZ : 0;

        float[] ModelMat = Mat4f.Create();
        Mat4f.Identity(ModelMat);
        Mat4f.Translate(ModelMat, ModelMat, 0, entity.CollisionBox.Y2 / 2, 0);

        double[] quat = Quaterniond.Create();
        Quaterniond.RotateX(quat, quat, entity.SidedPos.Pitch + (rotX * GameMath.DEG2RAD));
        Quaterniond.RotateY(quat, quat, entity.SidedPos.Yaw + ((rotY + 90) * GameMath.DEG2RAD));
        Quaterniond.RotateZ(quat, quat, entity.SidedPos.Roll + (rotZ * GameMath.DEG2RAD));

        float[] qf = new float[quat.Length];
        for (int k = 0; k < quat.Length; k++) qf[k] = (float)quat[k];
        Mat4f.Mul(ModelMat, ModelMat, Mat4f.FromQuat(Mat4f.Create(), qf));

        float scale = entity.Properties.Client.Size;

        Mat4f.Translate(ModelMat, ModelMat, 0, -entity.CollisionBox.Y2 / 2, 0f);
        Mat4f.Scale(ModelMat, ModelMat, new float[] { scale, scale, scale });
        Mat4f.Translate(ModelMat, ModelMat, -0.5f, 0, -0.5f);

        tmpModelMat
            .Set(ModelMat)
            .Mul(apap.AnimModelMatrix)
            .Translate(ap.PosX / 16f, ap.PosY / 16f, ap.PosZ / 16f)
        ;

        EntityPos entityPos = entity.SidedPos;

        float[] endVec = Mat4f.MulWithVec4(tmpModelMat.Values, hitboxOff);

        float motionX = endVec[0] - (entity.CollisionBox.X1 - entity.OriginCollisionBox.X1);
        float motionZ = endVec[2] - (entity.CollisionBox.Z1 - entity.OriginCollisionBox.Z1);

        if (Math.Abs(motionX) > 0.00001 || Math.Abs(motionZ) > 0.00001)
        {
            EntityPos posMoved = entityPos.Copy();
            posMoved.Motion.X = motionX;
            posMoved.Motion.Z = motionZ;

            moveDelta.Set(posMoved.Motion.X, posMoved.Motion.Y, posMoved.Motion.Z);

            collisionTester.ApplyTerrainCollision(entity, posMoved, dtFac, ref newPos);

            double reflectX = ((newPos.X - entityPos.X) / dtFac) - motionX;
            double reflectZ = ((newPos.Z - entityPos.Z) / dtFac) - motionZ;

            entityPos.Motion.X = reflectX;
            entityPos.Motion.Z = reflectZ;

            entity.CollisionBox.Set(entity.OriginCollisionBox);
            entity.CollisionBox.Translate(endVec[0], 0, endVec[2]);

            entity.SelectionBox.Set(entity.OriginSelectionBox);
            entity.SelectionBox.Translate(endVec[0], 0, endVec[2]);
        }
    }

    protected void callOnEntityInside()
    {
        var pos = entity.ServerPos;
        var world = entity.World;
        Cuboidd entityBox = collisionTester.entityBox;
        entityBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);
        entityBox.RemoveRoundingErrors(); // Necessary to prevent unwanted clipping through blocks when there is knockback

        BlockPos minPos = new BlockPos((int)entityBox.X1, (int)entityBox.Y1, (int)entityBox.Z1, pos.Dimension);
        BlockPos maxPos = new BlockPos((int)entityBox.X2, (int)entityBox.Y2, (int)entityBox.Z2, pos.Dimension);
        world.BlockAccessor.WalkBlocks(minPos, maxPos, (block, x, y, z) =>             // Although WalkBlocks is a heavy method, it makes efficient use of chunk fetching and caching, so that Chunk.Unpack() is called only once usually even if the entity spans 4 or 8 block positions
        {
            if (block.Id != 0)
            {
                minPos.Set(x, y, z);
                block.OnEntityInside(world, entity, minPos);
            }
        });
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        if (sapi != null) sapi.Server.RemovePhysicsTickable(this);
    }

    public override string PropertyName()
    {
        return "entitycontrolledphysics";
    }
}
