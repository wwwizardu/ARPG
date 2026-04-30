#nullable enable
using ARPG.Base;
using ARPG.Component;
using ARPG.Village;
using UnityEngine;

namespace ARPG.UI
{
    /// <summary>
    /// Phase D: 여관 UI (InnBed 진입, HasObjectSet(Inn) 확인 — 마을 전체 + Hearth 필요).
    /// 기능:
    ///   1. 세이브 (AR.s.Data.Save())
    ///   2. 휴식 (HP/MP 100% + 게임시간 +6h)
    ///   3. (Phase F+) 빠른 이동 — 다른 마을 InnBed로
    ///
    /// 비용 (Tier별 차등):
    ///   - Hamlet: 10G / Village: 25G / Town: 50G
    ///
    /// Step 10 MVP: 인터페이스/스켈레톤만. 실 휴식·세이브 처리는 Step U1 후.
    /// </summary>
    public class UIInn : UIBaseForm
    {
        // [SerializeField] private Button _restButton = null!;
        // [SerializeField] private Button _saveButton = null!;

        private const int REST_HOURS = 6;

        private int _innEntityId = -1;
        private int _villageId = -1;
        private bool _hasInnSet = false;

        public override void Initialize(string inName, bool isForm = false)
        {
            base.Initialize(inName, isForm);
        }

        public void Bind(int innEntityId)
        {
            _innEntityId = innEntityId;

            if (AR.s.Component.TryGetComponent<PlacedObjectComponent>(_innEntityId, out var po) == false)
            {
                Debug.LogWarning($"[UIInn] Bind 실패 — entityId={innEntityId} PlacedObject 없음");
                return;
            }
            _villageId = po.VillageId;

            // Inn 세트는 마을 전체 검사 (anchor 무시)
            _hasInnSet = AR.s.Village.HasObjectSet(_villageId, ObjectSetType.Inn);

            RefreshAll();
        }

        // ========== 액션 stub ==========

        public void OnClickRest()
        {
            if (_hasInnSet == false) return;
            // TODO: Gold 차감 (Stage 기반 가격)
            // TODO: HP/MP 100% 회복
            // TODO: AR.s.Time에 +6h 진행 (게임시간)
            Debug.Log($"[Inn] v{_villageId} 휴식 (+{REST_HOURS}h)");
        }

        public void OnClickSave()
        {
            // TODO: AR.s.Data.Save() 호출
            Debug.Log($"[Inn] v{_villageId} 세이브");
        }

        // ========== 갱신 hook ==========

        private void RefreshAll()
        {
            // TODO(Step U1+): Inn 세트 미보유 시 버튼 비활성, 가격 표시 등
        }

        public int GetRestCost()
        {
            // 마을 Stage 기반 차등 (Hamlet 10 / Village 25 / Town 50 / City 100)
            // TODO(Step U1+): VillageData.Stage 조회 후 dispatch
            return 10;
        }
    }
}
