#nullable enable
using UnityEngine;
using ARPG;

namespace ARPG.Skill
{
    public class Skill_Strike : SkillBase
    {
        private Vector3 _targetPosition; // 스킬 시작 시 타겟 위치
        public override void Initialize(Creature.CharacterBase inCharacter, SkillController inController, int inSkillId)
        {
            base.Initialize(inCharacter, inController, inSkillId);
        }

        public override bool StartSkill(byte inTargetType, long inTargetId)
        {
            base.StartSkill(inTargetType, inTargetId);

            // 마우스 위치 저장
            if (_character == null)
            {
                Debug.LogError("[Skill_Strike] StartSkill - _character is null");
                return false;
            }

            if (_character is Creature.ArpgPlayer)
            {
                Vector3 mouseScreenPosition = UnityEngine.Input.mousePosition;
                _targetPosition = Camera.main.ScreenToWorldPoint(new Vector3(mouseScreenPosition.x, mouseScreenPosition.y, Camera.main.nearClipPlane));
                _targetPosition.z = 0f; // 2D이므로 z값은 0으로 설정
            }
            else if (_character is Creature.Monster)
            {
                // 몬스터의 경우 플레이어를 향해 공격
                Creature.ArpgPlayer? player = AR.s.MyPlayer;
                if (player != null)
                {
                    _targetPosition = player.transform.position;
                }
                else
                {
                    Debug.LogError("[Skill_Strike] StartSkill - Player not found for Monster attack");
                    return false;
                }
            }
            else if (_character is Creature.Npc npc)
            {

            }
            else
            {
                Debug.LogError("[Skill_Strike] StartSkill - Unsupported character type");
                return false;
            }

            Creature.Animation attackAnimation = Creature.Animation.Attack;

#if UNITY_EDITOR
            if (_table.DamageTime < 0 || _table.Duration < _table.DamageTime)
            {
                Debug.LogError("[SkillSwing] StartSkill - attackAnimation.Duration is wrong");
            }
#endif
            // 스킬 속도는 1초에 한번을 기준으로 맞춘다. SwingSpeedStat에 따라 증가함
            float animationSpeed = _character.Stat.GetAttackSpeedMultiplier() * 0.01f;
            _character.PlayAnimation(attackAnimation, false, 1, animationSpeed);

            //Debug.Log($"[SkillSwing] StartSkill - Time({Time.time})");
            return true;
        }

        public override void EndSkill()
        {
            base.EndSkill();
            
            // 히트 체크 수행
            CheckHit();
        }
        
        protected override void OnHitTarget(GameObject target)
        {
            base.OnHitTarget(target);

            // 히트 처리 로직
            Debug.Log($"[Skill_Strike] Hit target: {target.name}");
        }

        private void CheckHit()
        {
            if (_character == null)
                return;

            Vector3 characterPosition = _character.transform.position;
            Vector3 attackDirection = (_targetPosition - characterPosition).normalized;

            // 공격 범위 내의 모든 콜라이더 검색
            Collider2D[] hitColliders = Physics2D.OverlapCircleAll(characterPosition, _table.SkillTargetRange1);

            foreach (Collider2D collider in hitColliders)
            {
                if (collider.gameObject == _character.gameObject)
                    continue; // 자기 자신은 제외

                if (IsWithinAttackAngle(characterPosition, collider.transform.position, attackDirection))
                {
                    // IHittable 인터페이스를 가진 대상만 처리
                    IHittable hittable = collider.GetComponent<IHittable>();
                    if (hittable != null)
                    {
                        // 같은 팀은 공격하지 않음
                        if (hittable.Team == _character.Team && hittable.Team != GlobalEnum.TeamType.None)
                        {
                            continue;
                        }

                        // 데미지 계산 (AttackMin과 AttackMax 사이의 랜덤값)
                        var damageBase = _character.GetAttackDamage();
                        int damage = UnityEngine.Random.Range(damageBase.Item1, damageBase.Item2 + 1);

                        // IHittable의 OnHit 함수 호출
                        hittable.OnHit(_character, true, GlobalEnum.DamageType.Physics, damage);
                    }
                }
            }
        }

        private bool IsWithinAttackAngle(Vector3 attackerPos, Vector3 targetPos, Vector3 attackDirection)
        {
            Vector3 toTarget = (targetPos - attackerPos).normalized;
            float angle = Vector3.Angle(attackDirection, toTarget);
            
            return angle <= _table.SkillTargetRange2;
        }



    }
}
