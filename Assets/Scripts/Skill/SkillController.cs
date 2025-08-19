#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Skill
{
    public class SkillController : MonoBehaviour
    {
        public enum BaseSkillType // 기본 스킬
        {
            Attack,
            UseItem,
        }

        public enum SkillTargetType
        {
            None,
            Block,
        }

        private const string _attackSkillLabel = "skill_attack_10";
        private const string _useItemSkillLabel = "skill_item_use";

        private Creature.CharacterBase? _character;
        private List<SkillBase> _activeSkillList = new(); // 액티브 스킬 리스트
        private List<SkillBase> _passiveSkillList = new(); // 액티브 스킬 리스트
        private Dictionary<int, SkillBase> _skillDic = new();
        private Dictionary<BaseSkillType, SkillBase> _baseSkillDic = new();

        private SkillBase? _currentSkill = null;
        
        private bool _waitServerResponse = false;

        public SkillBase? CurrentSkill { get { return _currentSkill; } }

        // public override void OnNetworkSpawn()
        // {
        //     base.OnNetworkSpawn();
        // }

        public void Initialize(Creature.CharacterBase inCharacter /*, Shared.Persistent.Data.SkillSaveData? inSkillDatas*/)
        {
            _character = inCharacter;
            _skillDic.Clear();
            _baseSkillDic.Clear();
            _activeSkillList.Clear();
            _passiveSkillList.Clear();

            // 기본 스킬을 추가한다.
            CreateBaseSkill();
            
            // if (inSkillDatas?.SkillData != null)
            // {
            //     for (int i = 1; i < inSkillDatas.SkillData.Length + 1; i++)
            //     {
            //         AddSkill(i, inSkillDatas.SkillData[i].MasterId);
            //     }
            // }
        }

        public SkillBase? AddSkill(int inIndex, int inSkillMasterId)
        {
            SkillBase? skill = null;
            // if (_skillDic.ContainsKey(inSkillMasterId) == false)
            // {
            //     SkillDataInfo? skillDataInfo = Hub.s.dataman.ExcelDataManager.GetSkillDataInfo(inSkillMasterId);
            //     if (skillDataInfo == null)
            //     {
            //         Debug.LogError($"[SkillController] CreateSkill - skillData not found, MasterId({inSkillMasterId})");
            //         return null;
            //     }

            //     skill = CreateSkill(skillDataInfo);
            //     if(skill == null)
            //         return null;

            //     if(skillDataInfo.IsPassive == true)
            //     {
            //         _passiveSkillList.Add(skill);
            //     }
            //     else
            //     {
            //         _activeSkillList.Add(skill);
            //     }

            //     _skillDic.Add(inSkillMasterId, skill);
            // }

            UpdateSkills();

            return skill;
        }

        public void RemoveSkill(int inIndex, int inSkillMasterId)
        {
            if(_skillDic.ContainsKey(inSkillMasterId) == false)
            {
                Debug.LogError($"[SkillController] RemoveSkill - cannot find skill, SkillMasterId({inSkillMasterId})");
                return;
            }

            SkillBase? skill = _skillDic[inSkillMasterId];
            if(skill == null)
            {
                Debug.LogError($"[SkillController] RemoveSkill - skill is null, SkillMasterId({inSkillMasterId})");
                return;
            }

            // if(skill.Table.IsPassive == true)
            // {
            //     _passiveSkillList.Remove(skill);
            // }
            // else
            // {
            //     _activeSkillList.Remove(skill);
            // }

            _skillDic.Remove(inSkillMasterId);

            UpdateSkills();
        }

        public void ClearSkill()
        {
            _passiveSkillList.Clear();
            _activeSkillList.Clear();
            _skillDic.Clear();
            _baseSkillDic.Clear();

            UpdateSkills();
        }

        public void UpdateSkills()
        {
            //for (int i = 0; i < _activeSkillList.Count; i++)
            //{
            //    SkillDataInfo? skillDataInfo = Hub.s.dataman.ExcelDataManager.GetSkillDataInfo(_activeSkillList[i]);
            //    if (skillDataInfo != null)
            //    {
            //        CreateSkill(skillDataInfo);
            //    }
            //}
        }

        public void StopAllSkill()
        {
            for (int i = 0; i < _activeSkillList.Count; i++)
            {
                _activeSkillList[i].Stop();
            }
        }

        public SkillBase? GetSkill(int inSkillMasterId)
        {
            if (_skillDic.TryGetValue(inSkillMasterId, out var skill) == false)
                return null;

            return skill;
        }
            
        public void OnCompleteSkill(int inSkillId)
        {
            _currentSkill = null;
            _character?.OnCompleteSkill(inSkillId);
        }

        private void FixedUpdate()
        {
            for (int i = 0; i < _activeSkillList.Count; i++)
            {
                if(_activeSkillList[i].IsRunning == true)
                {
                    _activeSkillList[i].SkillUpdate(Time.fixedDeltaTime);
                }
            }
        }

        public bool StartBaseSkill(BaseSkillType inType)
        {
            if(_character == null || _character.State == Creature.CharacterConditions.Dead)
                return false;

            if (_waitServerResponse == true)
                return false;

            try
            {
                if (_baseSkillDic.TryGetValue(inType, out SkillBase skill) == false)
                    return false;

                if (skill == null)
                    return false;

                if (skill.EnableSkill() == false)
                    return false;

                _waitServerResponse = true;

                // var targetInfo = GetTarget(skill.Table.MasterId);
                // StartSkillServerRpc(skill.Table.MasterId, targetInfo.Item1, targetInfo.Item2);
            }
            catch
            {
                _waitServerResponse = false;
            }

            return true;
        }

        public bool StartSkill(int inSkillId)
        {
            if (_waitServerResponse == true)
                return false;

            try
            {
                if (_skillDic.TryGetValue(inSkillId, out SkillBase skill) == false)
                    return false;

                if (skill == null)
                    return false;

                if (skill.EnableSkill() == false)
                    return false;

                _waitServerResponse = true;

                var targetInfo = GetTarget(inSkillId);
                // StartSkillServerRpc(inSkillId, targetInfo.Item1, targetInfo.Item2);
            }
            catch
            {
                _waitServerResponse = false;
            }
            
            return true;
        }

        public void SetSkillDamageToBlock(int inSkillId, byte inTargetType, long inTargetId, int damage)
        {
            // if (IsServer == false)
            //     return;

            // if(inTargetType == (byte)SkillTargetType.None)
            // {

            // }
            // else if(inTargetType == (byte)SkillTargetType.Block)
            // {
            //     bool result = Hub.s!.mapMgr.CheckTileChangeState((ulong)inTargetId, out ulong resultData);

            //     if(result)
            //     {
            //         if(Hub.s.mapMgr.UpdateTile((ulong)resultData, damage) == true) // 블록이 파괴되었다면 
            //         {
            //             BlockData_MasterData blockData = Hub.s.mapMgr.GetBlockData((ulong)inTargetId);
            //             if(blockData != null)
            //             {
            //                 Hub.s.achievementMgr.SetAchievement_Mining(_character!.CharacterId, blockData);

            //                 BSNEventManager.TriggerMiningCompleted(blockData.label);
            //             }
            //         }
            //     }
            //     else
            //     {
            //         //Failed
            //     }

            //     SetSkillDamageToBlockClientRpc(result);


            //     float consumeStress = _character.Status.GetFloat(Shared.Status.StatusPropertyType.StressDecPerHitStat);
            //     _character.Status.Consume(Shared.Status.StatusPropertyType.StressCurrent, consumeStress);
            //     // 블럭을 타격했을 때마다 허기 감소
            //     float consumeHungry = _character.Status.GetFloat(Shared.Status.StatusPropertyType.EnergyDecPerHitStat);
            //     _character.Status.Consume(Shared.Status.StatusPropertyType.EnergyCurrent, consumeHungry);
            // }
        }

        public (byte, long) GetTarget(int inSkillId)
        {
            SkillBase? skillBase = GetSkill(inSkillId);
            if(skillBase == null)
            {
                Debug.LogError("[SkillController] GetTarget - skillBase is null");
                return (0, 0);
            }

            // if(_character is Player player)
            // {
            //     IHittable hittableObject = player.GetMouseTargetingObject();
            //     if (hittableObject == null)
            //         return (0, 0);

            //     //!
            //     TileInteractionType interactionType = TileInteractionType.HitTileByHand;
            //     var itemInstance = player.Toolbelt.HoldingItemInstance;
            //     if (itemInstance != null)
            //     {
            //         if(itemInstance.ItemToolComponent != null)
            //         {
            //             if (itemInstance.ItemToolComponent.ToolCategory == "mining")
            //                 interactionType = TileInteractionType.HitTileByPickAxe;
            //         }
            //     }

            //     long data = (long)MapPositionConverter.CreateTileData(hittableObject, interactionType);

            //     return ((byte)SkillTargetType.Block, data);
            // }

            //Other character
            return (0, 0);

            //Map.Block block = GetBlockAtMousePosition();
            //if (block == null)
            //    return (0, 0);

            //long targetId = (long)Hub.s.mapMgr.GetBlockStateChangedData(block, skillBase.Damage);

            //return ((byte)SkillTargetType.Block, targetId);
        }

        public void OnChangeMovementState(Creature.MovementStates inPerv, Creature.MovementStates inNew)
        {
            if(CurrentSkill != null)
            {
                if (inNew != Creature.MovementStates.Idle)
                {
                    // CurrentSkill.SetCharacterMoved();
                }
            }
        }

        private void CreateBaseSkill()
        {
            // // 공격 스킬
            // SkillDataInfo? swingSkill = Hub.s.dataman.ExcelDataManager.GetSkillDataInfo(_attackSkillLabel);
            // if (swingSkill != null)
            // {
            //     Skillbase? skill = AddSkill(0, swingSkill.MasterId);
            //     if(skill != null)
            //     {
            //         _baseSkillDic.Add(BaseSkillType.Attack, skill);
            //     }
            // }

            // // 아이템 사용 스킬
            // SkillDataInfo? useItemSkill = Hub.s.dataman.ExcelDataManager.GetSkillDataInfo(_useItemSkillLabel);
            // if (useItemSkill != null)
            // {
            //     Skillbase? skill = AddSkill(0, useItemSkill.MasterId);
            //     if (skill != null)
            //     {
            //         _baseSkillDic.Add(BaseSkillType.UseItem, skill);
            //     }
            // }
        }

        // private SkillBase? CreateSkill(SkillDataInfo skillDataInfo)
        // {
        //     SkillBase? skill = null;
        //     if (skillDataInfo.Label == _attackSkillLabel)
        //     {
        //         skill = new SkillSwing();
        //     }
        //     else if (skillDataInfo.Label == _useItemSkillLabel)
        //     {
        //         skill = new SkillUseItem();
        //     }
        //     else
        //     {
        //         Debug.LogError($"[SkillController] CreateSkill - wrond skill Type, Label({skillDataInfo.Label})");
        //     }

        //     skill?.Initialize(_character, this, skillDataInfo.MasterId);

        //     return skill;
        // }

        // [ServerRpc(RequireOwnership = true)]
        // private void StartSkillServerRpc(int inSkillId, byte inTargetType, long inTargetId)
        // {
        //     MetricsLogger.MeasurePacketSize(nameof(StartSkillServerRpc), writer =>
        //     {
        //         writer.WriteValueSafe(inSkillId);
        //         writer.WriteValueSafe(inTargetType);
        //         writer.WriteValueSafe(inTargetId);
        //     });

        //     _character?.OnStartSkill(inSkillId);
        //     StartSkillClientRpc(inSkillId, inTargetType, inTargetId);
        // }

        // [ClientRpc]
        // private void StartSkillClientRpc(int inSkillId, byte inTargetType, long inTargetId) 
        // {
        //     _waitServerResponse = false;

        //     for (int i = 0; i < _activeSkillList.Count; i++)
        //     {
        //         if (_activeSkillList[i].Id == inSkillId)
        //         {
        //             _currentSkill = _activeSkillList[i];
        //             _activeSkillList[i].StartSkill(inTargetType, inTargetId);
        //             return;
        //         }
        //     }
        // }

        // [ClientRpc]
        // private void SetSkillDamageToBlockClientRpc(bool inResult)
        // {
        //     if (inResult == true)
        //     {
        //         // 내구도 체크
        //         CheckDurability();
                
        //         //Hub.s.mapMgr.ChangeBlockState(inTileData);
        //     }
        // }
    }  
}


