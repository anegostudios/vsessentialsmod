using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

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
        List<EmotionState> availableStates = new List<EmotionState>();
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
            
            foreach (JsonObject obj in availStates)
            {
                EmotionState state = obj.AsObject<EmotionState>();
                availableStates.Add(state);
            }
        }

        
        public override void OnEntityReceiveDamage(DamageSource damageSource, float damage)
        {
            if (damageSource.Source == EnumDamageSource.Fall && entity.World.Config.GetString("creatureHostility") == "passive" && entity.World.Config.GetString("creatureHostility") == "off")
            {
                return;
            }

            var beh = entity.GetBehavior<EntityBehaviorHealth>();
            healthRel = beh.Health / beh.MaxHealth;

            long sourceEntityId = damageSource.SourceEntity?.EntityId ?? 0;


            if (TryTriggerState("alarmherdondamage", sourceEntityId) && damageSource.SourceEntity != null && (entity as EntityAgent).HerdId > 0)
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

            for (int stateid = 0; stateid < availableStates.Count; stateid++)
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
                if (newstate.BelowTempThreshold > -99 && entity.World.BlockAccessor.GetClimateAt(entity.Pos.AsBlockPos, EnumGetClimateMode.NowValues).Temperature < newstate.BelowTempDuration)
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
            tickAccum += deltaTime;
            if (tickAccum < 0.33) return;
            tickAccum = 0;

            List<string> activecodes = ActiveStatesByCode.Keys.ToList();

            float nowStressLevel = 0f;

            foreach (var code in activecodes) 
            {
                ActiveStatesByCode[code].Duration -= 10*deltaTime;
                float leftDur = ActiveStatesByCode[code].Duration;
                if (leftDur <= 0)
                {
                    ActiveStatesByCode.Remove(code);
                    
                    entityAttr.RemoveAttribute(code);
                    continue;
                }

                nowStressLevel += availableStates[ActiveStatesByCode[code].StateId].StressLevel;
            }

            float curlevel = entity.WatchedAttributes.GetFloat("stressLevel");
            if (nowStressLevel > 0)
            {
                entity.WatchedAttributes.SetFloat("stressLevel", Math.Max(curlevel, nowStressLevel));
            }
            else
            {
                curlevel = Math.Max(0, curlevel - 1 / 20f);
                entity.WatchedAttributes.SetFloat("stressLevel", curlevel);
            }


            if (entity.World.EntityDebugMode) {
                entity.DebugAttributes.SetString("emotionstates", string.Join(", ", activecodes));
            }
        }


        public override string PropertyName()
        {
            return "emotionstates";
        }


        
    }
}
