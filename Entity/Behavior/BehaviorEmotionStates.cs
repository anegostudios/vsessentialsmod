using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
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
        public EnumAccumType AccumType = EnumAccumType.Max;  // sum, max or noaccum

        public float whenHealthRelBelow = 999f;


        public string[] NotifyEntityCodes = new string[0];
        public float NotifyChances = 0;
        public float NotifyRange = 12;
    }

    public class EntityBehaviorEmotionStates : EntityBehavior
    {
        List<EmotionState> availableStates = new List<EmotionState>();
        public Dictionary<int, float> ActiveStatesById = new Dictionary<int, float>();

        TreeAttribute entityAttrById;
        TreeAttribute entityAttr;

        int timer = 0;

        public EntityBehaviorEmotionStates(Entity entity) : base(entity)
        {
            try
            {
                if (entity.Attributes.HasAttribute("emotionstatesById"))
                {
                    entityAttrById = (TreeAttribute)entity.Attributes["emotionstatesById"];
                    

                    foreach (var val in entityAttrById)
                    {
                        ActiveStatesById[val.Key.ToInt(0)] = (float)val.Value.GetValue();
                    }
                }
                else
                {
                    entity.Attributes["emotionstatesById"] = entityAttrById = new TreeAttribute();
                }

            } catch
            {
                entity.Attributes["emotionstatesById"] = entityAttrById = new TreeAttribute();
            }

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

        float healthRel;
        public override void OnEntityReceiveDamage(DamageSource damageSource, float damage)
        {
            if (damageSource.Source == EnumDamageSource.Fall && entity.World.Config.GetString("creatureHostility") == "passive" && entity.World.Config.GetString("creatureHostility") == "off")
            {
                return;
            }

            var beh = entity.GetBehavior<EntityBehaviorHealth>();
            healthRel = beh.Health / beh.MaxHealth;

            if (TryTriggerState("alarmherdondamage") && damageSource.SourceEntity != null && (entity as EntityAgent).HerdId > 0)
            {
                EmotionState state = availableStates.First((s) => s.Code == "alarmherdondamage");
                entity.World.GetNearestEntity(entity.ServerPos.XYZ, state.NotifyRange, state.NotifyRange, (e) =>
                {
                    EntityAgent agent = e as EntityAgent;
                    if (e.EntityId != entity.EntityId && agent != null && agent.Alive && agent.HerdId == (entity as EntityAgent).HerdId)
                    {
                        agent.GetBehavior<EntityBehaviorEmotionStates>().TryTriggerState("aggressiveondamage");
                    }

                    return false;
                });
            }


            if (TryTriggerState("aggressiveondamage"))
            {
                if (TryTriggerState("aggressivealarmondamage"))
                {

                }
            }

            if (TryTriggerState("fleeondamage"))
            {
                if (TryTriggerState("fleealarmondamage"))
                {

                }
            }
        }



        public bool TryTriggerState(string statecode)
        {
            bool triggered=false;

            for (int stateid = 0; stateid < availableStates.Count; stateid++)
            {
                EmotionState newstate = availableStates[stateid];
            
                if (newstate.Code != statecode || entity.World.Rand.NextDouble() > newstate.Chance) continue;

                if (newstate.whenHealthRelBelow < healthRel)
                {
                    continue;
                }

                float activedur = 0;

                foreach (int activestateid in ActiveStatesById.Keys)
                {
                    if (activestateid == stateid)
                    {
                        activedur = ActiveStatesById[stateid];
                        continue;
                    }

                    EmotionState activestate = availableStates[activestateid];

                    if (activestate.Slot == newstate.Slot)
                    {
                        // Active state has priority over this one
                        if (activestate.Priority > newstate.Priority) return false;
                        else
                        {
                            // New state has priority
                            ActiveStatesById.Remove(activestateid);
                            entityAttrById.RemoveAttribute(""+ activestateid);
                            entityAttr.RemoveAttribute(newstate.Code);
                            break;
                        }
                    }
                }

                float newDuration = 0;
                if (newstate.AccumType == EnumAccumType.Sum) newDuration = activedur + newstate.Duration;
                if (newstate.AccumType == EnumAccumType.Max) newDuration = Math.Max(activedur, newstate.Duration);
                if (newstate.AccumType == EnumAccumType.NoAccum) newDuration = activedur > 0 ? activedur : newstate.Duration;

                ActiveStatesById[stateid] = newDuration;
                entityAttrById.SetFloat(""+ stateid, newDuration);
                entityAttr.SetFloat(newstate.Code, newDuration);
                triggered = true;
            }

            return triggered;
        }


        public override void OnGameTick(float deltaTime)
        {
            timer++;
            if (timer % 10 != 0) return;

            List<int> active = ActiveStatesById.Keys.ToList();

            foreach (int stateid in active) 
            {
                ActiveStatesById[stateid] -= 10*deltaTime;
                float leftDur = ActiveStatesById[stateid];
                if (leftDur <= 0)
                {
                    ActiveStatesById.Remove(stateid);
                    entityAttrById.RemoveAttribute(""+ stateid);
                    entityAttr.RemoveAttribute(availableStates[stateid].Code);
                    continue;
                }

                entityAttrById.SetFloat(stateid+"", leftDur);
                entityAttrById.SetFloat(availableStates[stateid].Code, leftDur);
            }

            if (entity.World.EntityDebugMode) {
                entity.DebugAttributes.SetString("emotionstates", string.Join(", ", active));
            }
        }


        public override string PropertyName()
        {
            return "emotionstates";
        }


        
    }
}
