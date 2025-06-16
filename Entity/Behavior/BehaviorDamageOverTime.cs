using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Vintagestory.GameContent;

public class DamageOverTimeEffect
{
    public EnumDamageSource DamageSource;
    public EnumDamageType DamageType;
    public int DamageTier;
    public float Damage;

    public TimeSpan TickDuration;
    public TimeSpan PreviousTickTime;
    public int TicksLeft;

    public DamageOverTimeEffect()
    {

    }

    public void ToBytes(BinaryWriter writer)
    {
        writer.Write((byte)DamageSource);
        writer.Write((byte)DamageType);
        writer.Write(DamageTier);
        writer.Write(Damage);
        writer.Write(TickDuration.Ticks);
        writer.Write(PreviousTickTime.Ticks);
        writer.Write(TicksLeft);
    }
    public static DamageOverTimeEffect FromBytes(BinaryReader reader)
    {
        return new DamageOverTimeEffect
        {
            DamageSource = (EnumDamageSource)reader.ReadByte(),
            DamageType = (EnumDamageType)reader.ReadByte(),
            DamageTier = reader.ReadInt32(),
            Damage = reader.ReadSingle(),
            TickDuration = TimeSpan.FromTicks(reader.ReadInt64()),
            PreviousTickTime = TimeSpan.FromTicks(reader.ReadInt64()),
            TicksLeft = reader.ReadInt32()
        };
    }
}

public class BehaviorDamageOverTime : EntityBehavior
{
    public BehaviorDamageOverTime(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => "damageovertime";

    public List<DamageOverTimeEffect> ActiveEffects { get; } = new();

    public int ApplyEffect(
        EnumDamageSource damageSource,
        EnumDamageType damageType,
        int damageTier,
        float totalDamage,
        TimeSpan totalTime,
        int ticksNumber)
    {
        DamageOverTimeEffect effect = new()
        {
            DamageSource = damageSource,
            DamageType = damageType,
            DamageTier = damageTier,
            Damage = totalDamage / ticksNumber,
            TickDuration = totalTime / ticksNumber,
            PreviousTickTime = TimeSpan.FromMilliseconds(entity.World.ElapsedMilliseconds),
            TicksLeft = ticksNumber
        };
        ActiveEffects.Add(effect);
        return ActiveEffects.Count - 1;
    }

    public override void OnGameTick(float deltaTime)
    {
        if (!entity.Alive)
        {
            ActiveEffects.Clear();
            return;
        }
        
        TimeSpan delta = TimeSpan.FromSeconds(deltaTime);

        if (timeSinceLastTickSec < timeBetweenTicksSec)
        {
            timeSinceLastTickSec += delta;
            return;
        }
        timeSinceLastTickSec = TimeSpan.Zero;

        TimeSpan currentTime = TimeSpan.FromMilliseconds(entity.World.ElapsedMilliseconds);

        float totalHealthFutureGain = 0f;
        float healthVelocity = 0f;

        for (int index = ActiveEffects.Count - 1; index >= 0; index--)
        {
            DamageOverTimeEffect effect = ActiveEffects[index];

            TimeSpan elapsed = currentTime - effect.PreviousTickTime;
            if (elapsed >= effect.TickDuration)
            {
                entity.ReceiveDamage(new DamageSource()
                {
                    Source = effect.DamageSource,
                    Type = effect.DamageType,
                    DamageTier = effect.DamageTier,
                }, effect.Damage);

                effect.TicksLeft--;

                if (effect.TicksLeft <= 0)
                {
                    ActiveEffects.RemoveAt(index);
                }
                else
                {
                    effect.PreviousTickTime = currentTime;
                }
            }

            if (effect.DamageType == EnumDamageType.Heal)
            {
                totalHealthFutureGain += effect.Damage * effect.TicksLeft;
                healthVelocity += effect.Damage * effect.TicksLeft;
            }
            else
            {
                healthVelocity -= effect.Damage * effect.TicksLeft;
            }
        }

        var ebh = entity.GetBehavior<EntityBehaviorHealth>();
        if (ebh != null)
        {
            if (Math.Abs(totalHealthFutureGain) > 0.1)
            {
                ebh.FutureHealth = ebh.Health + totalHealthFutureGain;
            } else
            {
                ebh.FutureHealth = null;
            }

            ebh.HealthChangeVelocity = healthVelocity;
        }
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        timeBetweenTicksSec = TimeSpan.FromSeconds(attributes["timeBetweenTicks"].AsFloat(0.5f));

        TimeSpan currentTime = TimeSpan.FromMilliseconds(entity.World.ElapsedMilliseconds);
        byte[] data = entity.WatchedAttributes.GetBytes("damageovertime-activeeffects");
        if (data != null)
        {
            ActiveEffectsFromBytes(data);
            foreach (DamageOverTimeEffect effect in ActiveEffects)
            {
                effect.PreviousTickTime = currentTime;
            }
        }
    }

    public override void ToBytes(bool forClient)
    {
        entity.WatchedAttributes.SetBytes("damageovertime-activeeffects", ActiveEffectsToBytes());
    }

    private TimeSpan timeSinceLastTickSec = TimeSpan.Zero;
    private TimeSpan timeBetweenTicksSec = TimeSpan.FromSeconds(0.5f);

    private byte[] ActiveEffectsToBytes()
    {
        MemoryStream stream = new();
        BinaryWriter writer = new(stream);

        writer.Write(ActiveEffects.Count);
        foreach (DamageOverTimeEffect effect in ActiveEffects)
        {
            effect.ToBytes(writer);
        }

        return stream.ToArray();
    }
    private void ActiveEffectsFromBytes(byte[] bytes)
    {
        MemoryStream stream = new(bytes);
        BinaryReader reader = new(stream);

        ActiveEffects.Clear();

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            ActiveEffects.Add(DamageOverTimeEffect.FromBytes(reader));
        }
    }
}
