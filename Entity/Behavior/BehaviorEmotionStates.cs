using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent
{
    public enum EnumAccumType
    {
        Sum, Max, NoAccum
    }

    public class EmotionState
    {
        public string Code = "";
        public float Duration = 0;
        public float Chance = 0;
        public int Slot = 0;
        public float Priority = 0;
        public float StressLevel = 0;
        public EnumAccumType AccumType = EnumAccumType.Max;  // sum, max or noaccum

        public float whenHealthRelBelow = 999f;


        public string[] NotifyEntityCodes = new string[0];
        public float NotifyChances = 0;
        public float NotifyRange = 12;

        public float BelowTempDuration = 0;
        public float BelowTempThreshold = -9999;
    }

    public class ActiveEmoState
    {
        public int StateId;
        public float Duration;
        public long SourceEntityId;
    }

    public class EntityBehaviorEmotionStates : EntityBehavior
    {
        EmotionState[] availableStates;
        public Dictionary<string, ActiveEmoState> ActiveStatesByCode = new Dictionary<string, ActiveEmoState>();

        TreeAttribute entityAttr;
        float healthRel;

        float tickAccum;

        public EntityBehaviorEmotionStates(Entity entity) : base(entity)
        {
            if (entity.Attributes.HasAttribute("emotionstates"))
            {
                entityAttr = entity.Attributes["emotionstates"] as TreeAttribute; 
            } else
            {
                entity.Attributes["emotionstates"] = entityAttr = new TreeAttribute();
            }
        }


        public override void Initialize(EntityProperties properties, JsonObject typeAttributes)
        {
            base.Initialize(properties, typeAttributes);

            JsonObject[] availStates = typeAttributes["states"].AsArray();

            availableStates = new EmotionState[availStates.Length];
            int i = 0;
            foreach (JsonObject obj in availStates)
            {
                EmotionState state = obj.AsObject<EmotionState>();
                availableStates[i++] = state;
            }

            tickAccum = (float)(entity.World.Rand.NextDouble() * 0.33);  //spread out the ticking if a lot of entities load at the same time, such as at server start
        }

        
        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (damageSource.Source == EnumDamageSource.Fall && entity.World.Config.GetString("creatureHostility") == "passive" && entity.World.Config.GetString("creatureHostility") == "off")
            {
                return;
            }

            var beh = entity.GetBehavior<EntityBehaviorHealth>();
            healthRel = beh == null ? 1 : beh.Health / beh.MaxHealth;

            var damagedBy = damageSource.GetCauseEntity();
            long sourceEntityId = damagedBy?.EntityId ?? 0;


            if (TryTriggerState("alarmherdondamage", sourceEntityId) && damagedBy != null && (entity as EntityAgent).HerdId > 0)
            {
                EmotionState state = availableStates.First((s) => s.Code == "alarmherdondamage");
                entity.World.GetNearestEntity(entity.ServerPos.XYZ, state.NotifyRange, state.NotifyRange, (e) =>
                {
                    EntityAgent agent = e as EntityAgent;
                    if (e.EntityId != entity.EntityId && agent != null && agent.Alive && agent.HerdId == (entity as EntityAgent).HerdId)
                    {
                        agent.GetBehavior<EntityBehaviorEmotionStates>().TryTriggerState("aggressiveondamage", sourceEntityId);
                    }

                    return false;
                });
            }


            if (TryTriggerState("aggressiveondamage", sourceEntityId))
            {
                if (TryTriggerState("aggressivealarmondamage", sourceEntityId))
                {

                }
            }

            if (TryTriggerState("fleeondamage", sourceEntityId))
            {
                if (TryTriggerState("fleealarmondamage", sourceEntityId))
                {

                }
            }
        }

        public override void OnNoPath(Vec3d target)
        {
            TryTriggerState("nopathfrustrated", 0);
        }


        public bool IsInEmotionState(string statecode)
        {
            return ActiveStatesByCode.ContainsKey(statecode);
        }

        public ActiveEmoState GetActiveEmotionState(string statecode)
        {
            ActiveStatesByCode.TryGetValue(statecode, out var state);
            return state;
        }

        public bool TryTriggerState(string statecode, long sourceEntityId)
        {
            return TryTriggerState(statecode, entity.World.Rand.NextDouble(), sourceEntityId);
        }

        public bool TryTriggerState(string statecode, double chance, long sourceEntityId)
        {
            bool triggered=false;

            for (int stateid = 0; stateid < availableStates.Length; stateid++)
            {
                EmotionState newstate = availableStates[stateid];
            
                if (newstate.Code != statecode || chance > newstate.Chance) continue;

                if (newstate.whenHealthRelBelow < healthRel)
                {
                    continue;
                }

                ActiveEmoState activeState = null;

                foreach (var val in ActiveStatesByCode)
                {
                    if (val.Key == newstate.Code)
                    {
                        activeState = val.Value;
                        continue;
                    }

                    int activestateid = val.Value.StateId;
                    EmotionState activestate = availableStates[activestateid];
                    if (activestate.Slot == newstate.Slot)
                    {
                        // Active state has priority over this one
                        if (activestate.Priority > newstate.Priority) return false;
                        else
                        {
                            // New state has priority
                            ActiveStatesByCode.Remove(val.Key);
                            
                            entityAttr.RemoveAttribute(newstate.Code);
                            break;
                        }
                    }
                }

                float duration = newstate.Duration;
                if (newstate.BelowTempThreshold > -99 && entity.World.BlockAccessor.GetClimateAt(entity.Pos.AsBlockPos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, entity.World.Calendar.TotalDays).Temperature < newstate.BelowTempThreshold)
                {
                    duration = newstate.BelowTempDuration;
                }

                float newDuration = 0;
                if (newstate.AccumType == EnumAccumType.Sum) newDuration = activeState?.Duration ?? 0 + duration;
                if (newstate.AccumType == EnumAccumType.Max) newDuration = Math.Max(activeState?.Duration ?? 0, duration);
                if (newstate.AccumType == EnumAccumType.NoAccum) newDuration = activeState?.Duration > 0 ? activeState?.Duration ?? 0 : duration;

                if (activeState == null)
                {
                    ActiveStatesByCode[newstate.Code] = new ActiveEmoState() { Duration = newDuration, SourceEntityId = sourceEntityId, StateId = stateid };
                } else
                {
                    activeState.SourceEntityId = sourceEntityId;
                }
                
                
                entityAttr.SetFloat(newstate.Code, newDuration);
                triggered = true;
            }

            return triggered;
        }


        public override void OnGameTick(float deltaTime)
        {
            if ((tickAccum += deltaTime) < 0.33f) return;
            tickAccum = 0;

            float nowStressLevel = 0f;

            List<string> codesToRemove = null;
            foreach (var stateAndcode in ActiveStatesByCode) 
            {
                string code = stateAndcode.Key;
                ActiveEmoState state = stateAndcode.Value;
                float leftDur = state.Duration -= 10 * deltaTime;
                if (leftDur <= 0)
                {
                    if (codesToRemove == null) codesToRemove = new List<string>();
                    codesToRemove.Add(code);
                    
                    entityAttr.RemoveAttribute(code);
                    continue;
                }

                nowStressLevel += availableStates[state.StateId].StressLevel;
            }
            if (codesToRemove != null) foreach (string s in codesToRemove) ActiveStatesByCode.Remove(s);

            float curlevel = entity.WatchedAttributes.GetFloat("stressLevel");
            if (nowStressLevel > 0)
            {
                entity.WatchedAttributes.SetFloat("stressLevel", Math.Max(curlevel, nowStressLevel));
            }
            else
            {
                if (curlevel > 0) // no need to keep recalculating and setting it once it reaches 0
                {
                    curlevel = Math.Max(0, curlevel - deltaTime * 1.25f);
                    entity.WatchedAttributes.SetFloat("stressLevel", curlevel);
                }
            }


            if (entity.World.EntityDebugMode)
            {
                // expensive string operations
                entity.DebugAttributes.SetString("emotionstates", string.Join(", ", ActiveStatesByCode.Keys.ToList()));
            }
        }


        public override string PropertyName()
        {
            return "emotionstates";
        }


        
    }
}
