#nullable enable
using ARPG.Base;
using ARPG.Component;
using ARPG.Message;
using UnityEngine;
using UnityEngine.UI;

namespace ARPG.UI
{
    /// <summary>
    /// HP/MP바 비주얼 컴포넌트
    /// DamageMessage / ManaChangedMessage를 받아 fillAmount를 갱신.
    /// 프리팹에 붙여서 EntityBase의 자식으로 사용.
    ///
    /// 일반 엔티티: 만피(CurrentHp == MaxHp) 일 때 자동 숨김 — 빌딩 등 평소엔 안 보이고 깎이면 표시되는 UX.
    /// 플레이어: 만피여도 캔버스 항상 표시 (MP바 상시 노출용).
    /// 마법·희귀 몬스터: 만피여도 표시하고 HP바를 등급 색으로 칠함 (MONSTER_RARITY_DESIGN.md §6).
    /// 접사가 있는 몬스터: 만피여도 표시 — 같은 캔버스의 접사 이름표(MonsterAffixLabelView)가 보이도록.
    /// 데미지 텍스트는 DamageAmount > 0 일 때만 표시 (진행도 갱신용 0 데미지 메시지엔 미표시).
    /// </summary>
    public class HpBarView : MonoBehaviour, IEntityMessageHandler
    {
        [SerializeField] private GameObject _canvas = null!; // HP바 전체 캔버스 (회전/위치 조정용)
        [SerializeField] private Image _hpBar = null!;
        [SerializeField] private GameObject _mpBarContainer = null!; // MP바 전체 컨테이너 (MP바 숨김용)
        [SerializeField] private Image _mpBar = null!;
        [SerializeField, Min(0f)] private float _monsterHpBarPadding = 0.15f;

        // 등급 몬스터 HP바 색 (MONSTER_RARITY_DESIGN.md §6). 일반 몬스터·NPC·플레이어는 프리팹 색 유지
        private static readonly Color MagicHpColor = new Color(0.45f, 0.55f, 1f, 1f);
        private static readonly Color RareHpColor = new Color(1f, 0.85f, 0.25f, 1f);

        private bool _isPlayer;
        private bool _showAtFullHp; // 마법·희귀 몬스터: 만피여도 HP바 표시
        private int _entityId = -1;
        private EntityBase? _monster;
        private float _monsterBodyTop;

        public void RegisterTo(EntityBase entity)
        {
            ComponentManager cm = AR.s.Component;
            _entityId = entity.EntityId;
            _isPlayer = AR.s.Data != null && entity.EntityId == AR.s.Data.CurrentPlayerEntityId;
            _monster = cm.HasComponent<MonsterTag>(_entityId) == true ? entity : null;
            _monsterBodyTop = 0f;
            if (_monster != null && cm.TryGetComponent<ColliderComponent>(_entityId, out ColliderComponent collider) == true)
            {
                // MonsterTable.HitOffsetY + HitRadius로 몸통 상단 높이를 구한다 (팩토리 fallback 포함).
                _monsterBodyTop = collider.HitOffset.y + collider.HitRadius;
            }
            UpdateMonsterPosition();

            entity.RegisterMessageHandler<DamageMessage>(OnDamage);
            entity.RegisterMessageHandler<ManaChangedMessage>(OnManaChanged);

            // 플레이어는 캔버스 상시 노출 + 초기 fillAmount를 StatComponent로 동기화
            if (_isPlayer)
            {
                if (_canvas != null && _canvas.activeSelf == false)
                    _canvas.SetActive(true);

                if (cm.TryGetComponent<StatComponent>(entity.EntityId, out var stat))
                {
                    if (_hpBar != null)
                        _hpBar.fillAmount = stat.FinalMaxHp > 0 ? (float)stat.CurrentHp / stat.FinalMaxHp : 0f;
                    if (_mpBar != null)
                        _mpBar.fillAmount = stat.FinalMaxMp > 0 ? (float)stat.CurrentMp / stat.FinalMaxMp : 0f;
                }
            }
            else
            {
                // 일반 엔티티는 초기 숨김 (만피 상태로 시작하는 경우가 많으므로)
                // 마법·희귀 몬스터와 접사가 있는 몬스터는 만피여도 보여 교전 전에 등급·접사 이름표를 알린다
                bool isGradedMonster = ApplyArchetypeColor(entity.EntityId);
                _showAtFullHp = isGradedMonster == true || HasMonsterAffixes(cm, entity.EntityId) == true;
                if (_canvas != null && _canvas.activeSelf)
                {
                    _mpBarContainer.SetActive(false); // 일반 엔티티는 MP바 숨김
                    _canvas.SetActive(_showAtFullHp);
                }
            }
        }

        private void LateUpdate()
        {
            // System_Render의 이동/스케일/점프 반영 이후에 위치를 맞춘다.
            UpdateMonsterPosition();
        }

        private void UpdateMonsterPosition()
        {
            if (_monster == null || _canvas == null || _hpBar == null)
                return;

            SpriteRenderer spriteRenderer = _monster.SpriteRenderer;
            // 스프라이트 bounds에는 투명 여백도 포함되므로 높이는 테이블 값만 사용한다.
            // 스프라이트 Transform은 점프 시 발 기준 위치를 따라가는 용도로만 사용한다.
            float feetY = spriteRenderer != null ? spriteRenderer.transform.position.y : _monster.transform.position.y;
            float top = feetY + _monsterBodyTop;

            // 캔버스 내부 HP 이미지의 기존 오프셋을 빼고, 실제 HP바 중심을 목표 높이에 맞춘다.
            // 월드 좌표로 보정하므로 부모 스케일이 적용되어도 높이가 중복 배율되지 않는다.
            Transform canvasTransform = _canvas.transform;
            Vector3 position = canvasTransform.position;
            float padding = _monsterHpBarPadding * Mathf.Abs(_monster.transform.lossyScale.y);
            position.y += top + padding - _hpBar.transform.position.y;
            canvasTransform.position = position;
        }

        /// <summary>
        /// 마법·희귀 몬스터면 HP바를 등급 색으로 칠하고 true. 등급 컴포넌트가 없거나 다른 등급이면 false.
        /// </summary>
        private bool ApplyArchetypeColor(int entityId)
        {
            if (_hpBar == null ||
                AR.s.Component.TryGetComponent<MonsterArchetypeComponent>(entityId, out MonsterArchetypeComponent grade) == false)
            {
                return false;
            }

            switch (grade.Archetype)
            {
                case GlobalEnum.MonsterArchetype.Magic:
                    _hpBar.color = MagicHpColor;
                    return true;
                case GlobalEnum.MonsterArchetype.Rare:
                    _hpBar.color = RareHpColor;
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasMonsterAffixes(ComponentManager cm, int entityId)
        {
            return cm.TryGetComponent<MonsterAffixComponent>(entityId, out MonsterAffixComponent affixes) == true &&
                affixes.Count > 0;
        }

        private void OnDamage(DamageMessage msg)
        {
            if (_hpBar == null)
                return;

            UpdateMonsterPosition();

            float ratio = msg.MaxHp > 0 ? (float)msg.CurrentHp / msg.MaxHp : 0f;
            _hpBar.fillAmount = ratio;

            // 일반 엔티티만 만피 시 캔버스 숨김. 플레이어는 MP바 상시 노출을 위해 캔버스 토글 안 함.
            if (_canvas != null && _isPlayer == false)
            {
                bool shouldShow = _showAtFullHp == true || msg.CurrentHp < msg.MaxHp;
                if (_canvas.activeSelf != shouldShow)
                    _canvas.SetActive(shouldShow);
            }

            // 0 데미지(진행도/HP 동기화용 메시지)는 텍스트 표시 안 함
            if (msg.DamageAmount <= 0 && msg.IsEvaded == false && msg.IsBlocked == false)
                return;

            // 시야 밖에서 숨은 엔티티의 데미지 숫자는 띄우지 않는다 (FOG_OF_WAR_DESIGN §5).
            // 만피 몬스터는 HP 캔버스가 원래 꺼져 있으므로 캔버스 활성 여부가 아니라 안개 상태로 판단한다.
            if (AR.s.Component.TryGetComponent<FogVisibilityComponent>(_entityId, out FogVisibilityComponent fog) == true &&
                fog.IsVisible == false)
                return;

            Vector3 damagePos = _hpBar.transform.position;
            damagePos.y -= 0.25f; // HP바 아래에 데미지 텍스트 표시

            AR.s.FloatingText.ShowDamageText(
                damagePos,
                msg.DamageAmount,
                msg.DamageType,
                msg.IsCritical,
                msg.IsEvaded,
                msg.IsBlocked
            );
        }

        private void OnManaChanged(ManaChangedMessage msg)
        {
            if (_mpBar == null)
                return;

            float ratio = msg.MaxMp > 0 ? (float)msg.CurrentMp / msg.MaxMp : 0f;
            _mpBar.fillAmount = ratio;
        }
    }
}
