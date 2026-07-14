#nullable enable
using ARPG.Component;
using ARPG.Village;
using TMPro;
using UnityEngine;

namespace ARPG.Systems
{
    /// <summary>
    /// V4 (VILLAGE_REWORK_PLAN §7.1): 상호작용 가능 대상의 일관된 프롬프트 표시.
    /// System_VillageServiceProximity(0.3s)가 채운 PlayerNearbyServicesComponent에서
    /// ServiceUIRouter.TryGetBestCandidate로 F키가 실제로 열 대상을 골라 "[F] {프롬프트}"를
    /// 대상 위에 월드 캔버스로 띄운다 — 프롬프트에 보이는 대상과 입력 라우팅 결과가 항상 같다.
    ///
    /// 뷰 오브젝트는 FloatingTextManager와 같은 코드 생성 월드 캔버스 패턴을 사용한다
    /// (신규 프리팹·에셋 불필요). 시스템은 뷰 참조만 들고 게임 상태는 갖지 않는다.
    /// </summary>
    public class System_VillageInteractionPrompt : IUpdateSystem
    {
        // 도메인 대역 (CLAUDE.md): 60-64 Lifecycle (IUpdate) — Proximity(61) 다음
        public int Priority => 62;
        public float UpdateInterval => 0.1f;

        private const float CANVAS_SCALE = 0.01f;
        private const float PROMPT_Y_OFFSET = 1.2f;
        private const string INTERACT_KEY_LABEL = "F";   // InputSystem_Actions "Interact" 기본 바인딩

        private GameObject? _root;
        private TextMeshProUGUI? _text;

        public void OnCreate() { }

        public void OnUpdate(float inDeltaTime)
        {
            if (AR.s.Data == null)
            {
                Hide();
                return;
            }
            int playerEntityId = AR.s.Data.CurrentPlayerEntityId;
            if (playerEntityId < 0
                || AR.s.Component.TryGetComponent<PlayerNearbyServicesComponent>(
                    playerEntityId, out var nearby) == false
                || ServiceUIRouter.TryGetBestCandidate(nearby,
                    out ServiceUIRouter.Candidate candidate) == false)
            {
                Hide();
                return;
            }

            Vector3 anchor;
            if (candidate.HasTile == true)
            {
                anchor = new Vector3(candidate.TileX + 0.5f,
                    candidate.TileY + PROMPT_Y_OFFSET, 0f);
            }
            else if (candidate.EntityId >= 0
                && AR.s.Component.TryGetComponent<TransformComponent>(candidate.EntityId,
                    out var targetTr) == true)
            {
                anchor = new Vector3(targetTr.Position.x,
                    targetTr.Position.y + PROMPT_Y_OFFSET, 0f);
            }
            else
            {
                Hide();
                return;
            }

            Show($"[{INTERACT_KEY_LABEL}] {ServiceUIRouter.GetPrompt(candidate)}", anchor);
        }

        public void OnReset()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
                _text = null;
            }
        }

        private void Show(string message, Vector3 worldPosition)
        {
            if (_root == null)
                CreateView();
            if (_root == null || _text == null)
                return;

            _text.text = message;
            _root.transform.position = worldPosition;
            if (_root.activeSelf == false)
                _root.SetActive(true);
        }

        private void Hide()
        {
            if (_root != null && _root.activeSelf == true)
                _root.SetActive(false);
        }

        /// <summary>FloatingTextManager와 같은 코드 생성 월드 캔버스 — 프리팹 의존 없음.</summary>
        private void CreateView()
        {
            GameObject canvasGo = new GameObject("InteractionPromptCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            canvasGo.transform.localScale = Vector3.one * CANVAS_SCALE;

            GameObject textGo = new GameObject("PromptText");
            textGo.transform.SetParent(canvasGo.transform, false);
            RectTransform rt = textGo.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400f, 60f);

            TextMeshProUGUI tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 30f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(1f, 0.95f, 0.7f, 1f);
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = new Color32(0, 0, 0, 220);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;

            _root = canvasGo;
            _text = tmp;
            _root.SetActive(false);
        }
    }
}
