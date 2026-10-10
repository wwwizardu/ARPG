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
    /// 데미지 텍스트는 DamageAmount > 0 일 때만 표시 (진행도 갱신용 0 데미지 메시지엔 미표시).
    /// </summary>
    public class HpBarView : MonoBehaviour, IEntityMessageHandler
    {
        [SerializeField] private GameObject _canvas = null!; // HP바 전체 캔버스 (회전/위치 조정용)
        [SerializeField] private Image _hpBar = null!;
        [SerializeField] private GameObject _mpBarContainer = null!; // MP바 전체 컨테이너 (MP바 숨김용)
        [SerializeField] private Image _mpBar = null!;

        private bool _isPlayer;
        private int _entityId = -1;

        public void RegisterTo(EntityBase entity)
        {
            _entityId = entity.EntityId;
            _isPlayer = AR.s.Data != null && entity.EntityId == AR.s.Data.CurrentPlayerEntityId;

            entity.RegisterMessageHandler<DamageMessage>(OnDamage);
            entity.RegisterMessageHandler<ManaChangedMessage>(OnManaChanged);

            // 플레이어는 캔버스 상시 노출 + 초기 fillAmount를 StatComponent로 동기화
            if (_isPlayer)
            {
                if (_canvas != null && _canvas.activeSelf == false)
                    _canvas.SetActive(true);

                if (AR.s.Component.TryGetComponent<StatComponent>(entity.EntityId, out var stat))
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
                if (_canvas != null && _canvas.activeSelf)
                {
                    _mpBarContainer.SetActive(false); // 일반 엔티티는 MP바 숨김
                    _canvas.SetActive(false);
                }
            }
        }

        private void OnDamage(DamageMessage msg)
        {
            if (_hpBar == null)
                return;

            float ratio = msg.MaxHp > 0 ? (float)msg.CurrentHp / msg.MaxHp : 0f;
            _hpBar.fillAmount = ratio;

            // 일반 엔티티만 만피 시 캔버스 숨김. 플레이어는 MP바 상시 노출을 위해 캔버스 토글 안 함.
            if (_canvas != null && _isPlayer == false)
            {
                bool shouldShow = msg.CurrentHp < msg.MaxHp;
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
