#nullable enable
using ARPG.Base;
using ARPG.Message;
using UnityEngine;
using UnityEngine.UI;

namespace ARPG.UI
{
    /// <summary>
    /// HP바 비주얼 컴포넌트
    /// DamageMessage를 받아 HP바 UI를 업데이트
    /// 프리팹에 붙여서 EntityBase의 자식으로 사용.
    ///
    /// 만피(CurrentHp == MaxHp) 일 때 자동 숨김 — 빌딩 등 평소엔 안 보이고 깎이면 표시되는 UX.
    /// 데미지 텍스트는 DamageAmount > 0 일 때만 표시 (진행도 갱신용 0 데미지 메시지엔 미표시).
    /// </summary>
    public class HpBarView : MonoBehaviour, IEntityMessageHandler
    {
        [SerializeField] private GameObject _canvas = null!; // HP바 전체 캔버스 (회전/위치 조정용)
        [SerializeField] private Image _hpBar = null!;

        public void RegisterTo(EntityBase entity)
        {
            entity.RegisterMessageHandler<DamageMessage>(OnDamage);
        }

        private void OnDamage(DamageMessage msg)
        {
            if (_hpBar == null)
                return;

            float ratio = msg.MaxHp > 0 ? (float)msg.CurrentHp / msg.MaxHp : 0f;
            _hpBar.fillAmount = ratio;

            // 만피면 HP바 전체(캔버스) 숨김 — CurrentHp < MaxHp 일 때만 표시.
            // 빌딩 등 평소엔 안 보이고 데미지/진행 중일 때만 노출되는 UX.
            if (_canvas != null)
            {
                bool shouldShow = msg.CurrentHp < msg.MaxHp;
                if (_canvas.activeSelf != shouldShow)
                    _canvas.SetActive(shouldShow);
            }

            // 0 데미지(진행도/HP 동기화용 메시지)는 텍스트 표시 안 함
            if (msg.DamageAmount <= 0 && msg.IsEvaded == false && msg.IsBlocked == false)
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
    }
}
