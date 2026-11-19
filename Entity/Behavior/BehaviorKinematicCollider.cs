using System;
using System.Collections.Concurrent;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent;

/// <summary>
/// Supplies a custom rigid transform for an entity's kinematic collider.
/// </summary>
public interface IKinematicColliderTransformProvider
{
    ColliderTransform GetKinematicColliderTransform();
}

/// <summary>
/// Registers entity-local collision boxes as a transformed collider.
/// <br/>Uses the "kinematiccollider" code.
/// </summary>
/// <example><code lang="json">
/// "behaviors": [
///  {
///     "code": "kinematiccollider",
///     "collisionShape": {
///         "base": "entity/nonliving/platform",
///         "selectiveElements": [ "Floor*", "Rail*" ]
///     },
///     "carryEntities": true,
///     "rotateWithEntity": true
///  }
/// ]
/// </code></example>
[DocumentAsJson]
public class EntityBehaviorKinematicCollider : EntityBehavior, IRenderer, IEntityColliderProvider
{
    public const string DefaultColliderCode = "main";

    /// <summary>
    /// Axis-aligned collision boxes in coordinates local to the entity position.
    /// </summary>
    [DocumentAsJson("Optional")]
    public Cuboidf[] CollisionBoxes = null!;

    /// <summary>
    /// An optional model whose selected cuboid elements become this collider's boxes.
    /// </summary>
    [DocumentAsJson("Optional")]
    public CompositeShape? CollisionShape;

    /// <summary>
    /// Whether supported entities follow this collider and inherit its motion when leaving it.
    /// </summary>
    [DocumentAsJson("Optional", "true")]
    public bool CarryEntities = true;

    /// <summary>
    /// Whether supported entities should ignore liquid occupying the same world position, why have an option for this? Well someone may want a platform to work underwater!
    /// </summary>
    [DocumentAsJson("Optional", "false")]
    public bool KeepsSupportedEntitiesDry;

    /// <summary>
    /// Whether the collision boxes follow the entity pitch, yaw, and roll.
    /// </summary>
    [DocumentAsJson("Optional", "true")]
    public bool RotateWithEntity = true;

    /// <summary>
    /// Stable code used to resolve this collider's local registration on the client and server.
    /// </summary>
    [DocumentAsJson("Optional", DefaultColliderCode)]
    public string ColliderCode = DefaultColliderCode;

    /// <summary>
    /// Accepts collision layer names or an unsigned integer mask.
    /// </summary>
    [DocumentAsJson("Optional", "Solid")]
    public CollisionLayer CollisionLayer = CollisionLayer.Solid;

    /// <summary>
    /// Exclude Entity to keep these boxes queryable without blocking movement.
    /// </summary>
    [DocumentAsJson("Optional", "All")]
    public CollisionLayer CollisionMask = CollisionLayer.All;

    private int colliderDimension;
    private readonly double[] rotation = Quaterniond.Create();
    private Cuboidd[] localCollisionBoxes = null!;
    private IKinematicColliderTransformProvider? transformProvider;
    private ICoreClientAPI? capi;
    private ColliderTransform transform;
    private long colliderId;
    private bool hasTransform;
    private bool wasMoving;
    private bool disposed;

    public EntityBehaviorKinematicCollider(Entity entity) : base(entity) { }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        disposed = false;
        CollisionShape = attributes["collisionShape"].AsObject<CompositeShape>(null, entity.Code.Domain);
        Cuboidf?[]? collisionBoxes = attributes["collisionBoxes"].AsArray<Cuboidf>();
        if (CollisionShape != null && collisionBoxes != null)
        {
            throw new InvalidOperationException($"Entity {entity.Code} cannot define both kinematic collision boxes and a collision shape.");
        }

        if (CollisionShape != null)
        {
            CollisionBoxes = [];
            localCollisionBoxes = getShapeCollisionBoxes(properties, CollisionShape);
        }
        else if (collisionBoxes == null)
        {
            CollisionBoxes = [entity.CollisionBox.Clone()];
            localCollisionBoxes = [CollisionBoxes[0].ToDouble()];
        }
        else
        {
            if (collisionBoxes.Length == 0) throw new InvalidOperationException($"Entity {entity.Code} requires at least one kinematic collision box.");

            CollisionBoxes = new Cuboidf[collisionBoxes.Length];
            for (int i = 0; i < collisionBoxes.Length; i++)
            {
                CollisionBoxes[i] = collisionBoxes[i] ?? throw new InvalidOperationException($"Entity {entity.Code} has a null kinematic collision box.");
            }

            localCollisionBoxes = new Cuboidd[CollisionBoxes.Length];
            for (int i = 0; i < CollisionBoxes.Length; i++)
            {
                localCollisionBoxes[i] = CollisionBoxes[i].ToDouble();
            }
        }

        CollisionLayer = attributes["collisionLayer"].AsObject(CollisionLayer);
        CollisionMask = attributes["collisionMask"].AsObject(CollisionMask);
        CarryEntities = attributes["carryEntities"].AsBool(CarryEntities);
        KeepsSupportedEntitiesDry = attributes["keepsSupportedEntitiesDry"].AsBool(KeepsSupportedEntitiesDry);
        RotateWithEntity = attributes["rotateWithEntity"].AsBool(RotateWithEntity);
        ColliderCode = attributes["colliderCode"].AsString(ColliderCode);
        if (string.IsNullOrEmpty(ColliderCode)) throw new InvalidOperationException($"Entity {entity.Code} requires a non-empty kinematic collider code.");

        if (entity.Api is ICoreClientAPI clientApi)
        {
            capi = clientApi;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "kinematiccollider");
        }
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        if (RotateWithEntity) transformProvider = entity.GetInterface<IKinematicColliderTransformProvider>();
        UpdateCollider(0);
    }

    public override void OnGameTick(float deltaTime)
    {
        if (entity.Api.Side == EnumAppSide.Server) UpdateCollider(deltaTime);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (capi?.IsGamePaused == true) return;
        var player = capi?.World.Player.Entity;
        if (player?.MountedOn?.Entity == entity && player.MountedOn.MountSupplier.Controller == player) return;

        UpdateCollider(deltaTime);
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        capi?.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        if (colliderId == 0) return;

        entity.World.CollisionScene.UnregisterCollider(colliderId);
        colliderId = 0;
        hasTransform = false;
        wasMoving = false;
    }

    // Runs after entity interpolation and before player physics.
    public double RenderOrder => 0.01;

    public int RenderRange => 9999;

    /// <summary>
    /// The locally registered collider identifier, or zero while the collider is unavailable.
    /// </summary>
    public long ColliderId => colliderId;

    public bool TryGetColliderId(string colliderCode, out long id)
    {
        if (colliderId != 0 && colliderCode == ColliderCode)
        {
            id = colliderId;
            return true;
        }

        id = 0;
        return false;
    }

    public override string PropertyName() => "entitykinematiccollider";

    /// <param name="elapsedSeconds">Time since the previous published pose, used to derive carry velocity.</param>
    public void UpdateCollider(float elapsedSeconds)
    {
        if (disposed) return;
        ColliderTransform currentTransform = getTransform();
        bool transformChanged = hasTransform && !currentTransform.Equals(transform);
        if (hasTransform && !transformChanged && !wasMoving && colliderDimension == entity.Pos.Dimension) return;

        colliderDimension = entity.Pos.Dimension;
        transform = currentTransform;
        hasTransform = true;
        if (colliderId == 0)
        {
            colliderId = entity.World.CollisionScene.RegisterTransformedCollider(entity.Pos.Dimension, localCollisionBoxes, transform, CarryEntities, entity.EntityId, ColliderCode, KeepsSupportedEntitiesDry, CollisionLayer, CollisionMask);
            wasMoving = false;
            return;
        }

        entity.World.CollisionScene.UpdateColliderTransform(colliderId, entity.Pos.Dimension, transform, elapsedSeconds);
        wasMoving = transformChanged;
    }

    private ColliderTransform getTransform()
    {
        var pos = entity.Pos;
        if (!RotateWithEntity) return new ColliderTransform(pos);
        if (transformProvider != null) return transformProvider.GetKinematicColliderTransform();

        rotation[0] = 0;
        rotation[1] = 0;
        rotation[2] = 0;
        rotation[3] = 1;
        Quaterniond.RotateX(rotation, rotation, pos.Pitch);
        Quaterniond.RotateY(rotation, rotation, pos.Yaw);
        Quaterniond.RotateZ(rotation, rotation, pos.Roll);
        return new ColliderTransform(pos, rotation);
    }

    private Cuboidd[] getShapeCollisionBoxes(EntityProperties properties, CompositeShape collisionShape)
    {
        if (collisionShape.Base == null) throw new InvalidOperationException($"Entity {entity.Code} requires a base path for its kinematic collision shape.");
        if (collisionShape.Format != EnumShapeFormat.VintageStory) throw new InvalidOperationException($"Entity {entity.Code} requires a Vintage Story JSON kinematic collision shape.");
        if (collisionShape.Alternates != null || collisionShape.Overlays != null) throw new InvalidOperationException($"Entity {entity.Code} cannot use alternate or overlay shapes for its kinematic collider.");

        var cache = ObjectCacheUtil.GetOrCreate(entity.Api, "kinematicColliderShapeBoxes", () => new ConcurrentDictionary<string, Cuboidd[]>());
        string cacheKey = getShapeCacheKey(properties, collisionShape);
        return cache.GetOrAdd(cacheKey, _ =>
        {
            AssetLocation shapePath = collisionShape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            Shape? shape = Shape.TryGet(entity.Api, shapePath);
            if (shape?.Elements == null) throw new InvalidOperationException($"Entity {entity.Code} could not load kinematic collision shape {shapePath}.");

            for (int i = 0; i < shape.Elements.Length; i++) shape.Elements[i].ResolveReferences();
            Cuboidd[] boxes = ShapeColliderBuilder.Build(shape, collisionShape.SelectiveElements, collisionShape.IgnoreElements, getShapeTransform(properties, collisionShape));
            if (boxes.Length == 0) throw new InvalidOperationException($"Entity {entity.Code} kinematic collision shape {shapePath} did not select any solid cuboid elements.");
            return boxes;
        });
    }

    private static string getShapeCacheKey(EntityProperties properties, CompositeShape collisionShape)
    {
        return FormattableString.Invariant($"{collisionShape.Base}|{collisionShape.Format}|{collisionShape.rotateX:R},{collisionShape.rotateY:R},{collisionShape.rotateZ:R}|{collisionShape.offsetX:R},{collisionShape.offsetY:R},{collisionShape.offsetZ:R}|{collisionShape.Scale:R}|{properties.Client.Size:R}|{string.Join(';', collisionShape.SelectiveElements ?? [])}|{string.Join(';', collisionShape.IgnoreElements ?? [])}");
    }

    private static double[] getShapeTransform(EntityProperties properties, CompositeShape collisionShape)
    {
        double[] transform = Mat4d.Create();
        double entityScale = properties.Client.Size;
        Mat4d.Scale(transform, entityScale, entityScale, entityScale);
        Mat4d.Translate(transform, transform, -0.5, 0, -0.5);
        Mat4d.Translate(transform, transform, collisionShape.offsetX, collisionShape.offsetY, collisionShape.offsetZ);

        if (collisionShape.rotateX != 0 || collisionShape.rotateY != 0 || collisionShape.rotateZ != 0)
        {
            Mat4d.Translate(transform, transform, 0.5, 0.5, 0.5);
            Mat4d.RotateX(transform, transform, collisionShape.rotateX * GameMath.DEG2RAD);
            Mat4d.RotateY(transform, transform, collisionShape.rotateY * GameMath.DEG2RAD);
            Mat4d.RotateZ(transform, transform, collisionShape.rotateZ * GameMath.DEG2RAD);
            Mat4d.Translate(transform, transform, -0.5, -0.5, -0.5);
        }

        if (collisionShape.Scale != 1)
        {
            Mat4d.Translate(transform, transform, 0.5, 0, 0.5);
            Mat4d.Scale(transform, collisionShape.Scale, collisionShape.Scale, collisionShape.Scale);
            Mat4d.Translate(transform, transform, -0.5, 0, -0.5);
        }

        return transform;
    }
}
