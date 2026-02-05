using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;


namespace Vintagestory.GameContent
{
    public class GaitMeta : AnimationMetaData
    {
        public float YawMultiplier = 1f;
        public float MoveSpeed = 0f;
        public bool Backwards = false;
        // <summary> If true, the animal may use this gait of its own volition </summary>
        public bool Natural = true;
        public float StaminaCost = 0f;
        public string? FallbackGaitCode; // Gait to slow down to such as when fatiguing
        public AssetLocation? Sound;
        public EnumHabitat Environment = EnumHabitat.Land;

        public int Direction => MoveSpeed == 0f ? 0 : Backwards ? -1 : 1;
        public bool HasMotion => MoveSpeed > 0f;
        public bool HasForwardMotion => MoveSpeed > 0f && !Backwards;
        public bool HasBackwardMotion => MoveSpeed > 0f && Backwards;
    }

    public class EntityBehaviorGait : EntityBehavior
    {
        public override string PropertyName()
        {
            return "gait";
        }

        protected EntityAgent eagent = null!;

        public readonly FastSmallDictionary<string, GaitMeta> Gaits = new(1);
        protected GaitMeta currentGait = null!;
        public GaitMeta CurrentGait
        {
            get => currentGait;
            set
            {
                GaitMeta prevGait = currentGait;
                currentGait = value;
                if (entity.WatchedAttributes.GetString("currentgait") != value.Code)
                {
                    entity.WatchedAttributes.SetString("currentgait", value.Code);
                    OnGaitChangedFrom(prevGait);
                }
            }
        }
        protected string? currentTurnAnimation = null;
        // Current turning speed (rad/tick)
        public double AngularVelocity = 0.0;

        // Movement speed is multiplied by this
        public double MoveSpeedModifier = 1;

        protected ILoadedSound? gaitSound;

        public event Action? GaitChangedForEnvironmentDelegate;

        public FastSmallDictionary<EnumHabitat, GaitMeta> IdleGaits = new(1);
        public GaitMeta FallbackGait => CurrentGait.FallbackGaitCode == null ? IdleGaits[CurrentGait.Environment] : Gaits[CurrentGait.FallbackGaitCode];

        public float GetYawMultiplier() => CurrentGait?.YawMultiplier ?? 3.5f; // Default yaw multiplier if not set

        public virtual void SetIdle() => CurrentGait = IdleGaits.TryGetValue(CurrentGait.Environment) ?? IdleGaits[EnumHabitat.Land];
        public bool IsIdle => IsIdleGait(CurrentGait);

        public virtual bool IsIdleGait(GaitMeta gait)
        {
            return gait == IdleGaits.TryGetValue(gait.Environment);
        }

        public GaitMeta CascadingFallbackGait(int n)
        {
            var result = CurrentGait;

            while (n > 0)
            {
                result = result.FallbackGaitCode == null ? IdleGaits[result.Environment] : Gaits[result.FallbackGaitCode];
                n--;
            }

            return result;
        }

        public EntityBehaviorGait(Entity entity) : base(entity)
        {
            eagent = (EntityAgent)entity;
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            ArgumentNullException.ThrowIfNull(attributes);
            base.Initialize(properties, attributes);

            GaitMeta?[]? gaitarray = attributes["gaits"].AsArray<GaitMeta>();
            ArgumentNullException.ThrowIfNull(gaitarray);
            foreach (GaitMeta? gait in gaitarray)
            {
                ArgumentNullException.ThrowIfNull(gait);
                gait.ClientSide = true; // Opt out of animation syncing
                Gaits[gait.Code] = gait;
            }

            FastSmallDictionary<EnumHabitat, string>? idleGaitCodes = attributes["idleGaits"].AsObject<FastSmallDictionary<EnumHabitat, string>>();
            if (idleGaitCodes != null)
            {
                foreach (var entry in idleGaitCodes)
                {
                    IdleGaits[entry.Key] = Gaits[entry.Value];
                }
                if (!IdleGaits.ContainsKey(EnumHabitat.Land)) throw new ArgumentException("JSON error. An idle gait usable on land must be provided for {0}", entity.Code);
            }
            else
            {
                string? idleGaitCode = attributes["idleGait"]?.AsString("idle");
                GaitMeta idle;
                if (idleGaitCode == null || !Gaits.TryGetValue(idleGaitCode, out idle)) throw new ArgumentException("JSON error. No idle gait for {0}", entity.Code);
                IdleGaits[EnumHabitat.Land] = idle;
            }

            if (entity.Api.Side == EnumAppSide.Client)
            {
                // Only needed on the client, because on the server all changes will go through the CurrentGait property setter, which calls OnGaitChangedFrom directly
                entity.WatchedAttributes.RegisterModifiedListener("currentgait", OnGaitChanged);
            }
            CurrentGait = IdleGaits[EnumHabitat.Land];

            if (entity.Api is ICoreClientAPI capi)
            {
                capi.Event.PauseResume += OnGamePauseResume;
            }
        }

        protected virtual void OnGaitChanged()
        {
            OnGaitChangedFrom(currentGait);
        }

        protected virtual void OnGaitChangedFrom(GaitMeta? prevGait)
        {
            currentGait = Gaits[entity.WatchedAttributes.GetString("currentgait")];
            if (currentGait.Code == prevGait?.Code) return;

            ICoreClientAPI? capi = entity.Api as ICoreClientAPI;
            if (capi != null && CurrentGait.Sound != prevGait?.Sound)
            {
                gaitSound?.Stop();
                gaitSound?.Dispose();
                gaitSound = null;

                if (CurrentGait.Sound != null)
                {
                    gaitSound = capi.World.LoadSound(new SoundParams()
                    {
                        Location = CurrentGait.Sound!.Clone().WithPathPrefix("sounds/"),
                        DisposeOnFinish = false,
                        Position = entity.Pos.XYZ.ToVec3f(),
                        ShouldLoop = true
                    });
                }

                gaitSound?.Start();
            }

            if (CurrentGait.Animation != prevGait?.Animation)
            {
                if (prevGait?.Animation != null && prevGait.Animation != "jump") eagent.AnimManager.StopAnimation(prevGait.Animation); // Should this be checking something else, like looping settings?

                if (CurrentGait.Animation != null)
                {
                    eagent.AnimManager.StartAnimation(CurrentGait);
                    eagent.AnimManager.AnimationsDirty = true;
                }
            }

            if (CurrentGait.MoveSpeed == 0)
            {
                eagent.Controls.StopAllMovement();
                // Don't immediately set the walk and fly vectors to 0. They will decrease naturally from drag, which looks nicer.

                if (currentTurnAnimation != null)
                {
                    eagent.StopAnimation(currentTurnAnimation);
                    eagent.AnimManager.AnimationsDirty = true;
                    currentTurnAnimation = null;
                }
            }
        }

        protected virtual void OnGamePauseResume(bool isPaused)
        {
            if (isPaused)
            {
                gaitSound?.Pause();
            }
            else
            {
                if (gaitSound?.IsPaused == true) gaitSound?.Start();
            }
        }

        protected float notOnGroundAccum;
        public override void OnGameTick(float dt)
        {
            UpdateGaitForEnvironment();

            if (entity.Api.Side != EnumAppSide.Client) return;

            if (entity.OnGround) notOnGroundAccum = 0;
            else notOnGroundAccum += dt;

            if (gaitSound != null)
            {
                gaitSound.SetPosition((float)entity.Pos.X, (float)entity.Pos.Y, (float)entity.Pos.Z);

                if (notOnGroundAccum > 0.2)
                {
                    if (!gaitSound.IsPaused) gaitSound.Pause();
                }
                else if (gaitSound.IsPaused)
                {
                    gaitSound.Start();
                }
            }

            Move(dt);
            UpdateTurnAnimation();
        }

        public virtual void UpdateGaitForEnvironment()
        {
            EnumHabitat targetEnvironment = entity.Swimming ? EnumHabitat.Sea : EnumHabitat.Land;
            // No conditions for air or underwater implemented at this time

            if (CurrentGait.Environment == targetEnvironment) return;

            GaitMeta? closest = null;
            foreach (GaitMeta gait in Gaits.Values)
            {
                if (!gait.Natural || gait.Environment != targetEnvironment || gait.Direction != CurrentGait.Direction) continue;

                if (closest == null || (Math.Abs(gait.MoveSpeed - CurrentGait.MoveSpeed) < Math.Abs(closest.MoveSpeed - CurrentGait.MoveSpeed)))
                {
                    closest = gait;
                }
            }

            CurrentGait = closest;
            GaitChangedForEnvironmentDelegate?.Invoke();
        }

        protected virtual void Move(float dt)
        {
            EntityControls controls = eagent.Controls;

            double cosYaw = Math.Cos(entity.Pos.Yaw);
            double sinYaw = Math.Sin(entity.Pos.Yaw);
            controls.WalkVector.Set(sinYaw, 0, cosYaw);
            controls.WalkVector.Mul(CurrentGait.MoveSpeed * GlobalConstants.OverallSpeedMultiplier * CurrentGait.Direction * MoveSpeedModifier);

            // Make it walk along the wall, but not walk into the wall, which causes it to climb
            if (entity.Properties.RotateModelOnClimb && controls.IsClimbing && entity.ClimbingOnFace != null && entity.Alive)
            {
                BlockFacing facing = entity.ClimbingOnFace;
                if (Math.Sign(facing.Normali.X) == Math.Sign(controls.WalkVector.X))
                {
                    controls.WalkVector.X = 0;
                }

                if (Math.Sign(facing.Normali.Z) == Math.Sign(controls.WalkVector.Z))
                {
                    controls.WalkVector.Z = 0;
                }
            }

            if (entity.Swimming)
            {
                controls.FlyVector.Set(controls.WalkVector);

                Vec3d pos = entity.Pos.XYZ;
                Block inblock = entity.World.BlockAccessor.GetBlockRaw((int)pos.X, (int)(pos.Y), (int)pos.Z, BlockLayersAccess.Fluid);
                Block aboveblock = entity.World.BlockAccessor.GetBlockRaw((int)pos.X, (int)(pos.Y + 1), (int)pos.Z, BlockLayersAccess.Fluid);
                float waterY = (int)pos.Y + inblock.LiquidLevel / 8f + (aboveblock.IsLiquid() ? 9 / 8f : 0);
                float bottomSubmergedness = waterY - (float)pos.Y;

                // 0 = at swim line
                // 1 = completely submerged
                float swimlineSubmergedness = GameMath.Clamp(bottomSubmergedness - ((float)entity.SwimmingOffsetY), 0, 1);
                swimlineSubmergedness = Math.Min(1, swimlineSubmergedness + 0.075f);
                controls.FlyVector.Y = GameMath.Clamp(controls.FlyVector.Y, 0.02f, 0.04f) * swimlineSubmergedness*3;

                if (entity.CollidedHorizontally)
                {
                    controls.FlyVector.Y = 0.05f;
                }

                eagent.Pos.Motion.Y += (swimlineSubmergedness-0.1)/300.0;
            }
        }

        protected virtual void UpdateTurnAnimation()
        {
            string? nowTurnAnim = null;
            if (!CurrentGait.HasBackwardMotion)
            {
                if (AngularVelocity > 0.001)
                {
                    nowTurnAnim = "turn-left";
                }
                else if (AngularVelocity < -0.001)
                {
                    nowTurnAnim = "turn-right";
                }
            }

            if (nowTurnAnim != currentTurnAnimation)
            {
                if (currentTurnAnimation != null)
                {
                    eagent.StopAnimation(currentTurnAnimation);
                    eagent.AnimManager.AnimationsDirty = true;
                }
                if (nowTurnAnim == null)
                {
                    currentTurnAnimation = null;
                }
                else
                {
                    var anim = (CurrentGait.HasMotion == false ? "idle-" : "") + nowTurnAnim;
                    currentTurnAnimation = anim;
                    eagent.StartAnimation(anim);
                }
            }
        }
    }
}
