#nullable enable
using ARPG.Base;
using ARPG.Message;
using UnityEngine;
using UnityEngine.UI;

namespace ARPG.OreVein
{
    /// <summary>
    /// 플레이어 채광 임시 진행 막대 (WORLD_ORE_VEIN_PLAN §7.1). 판단은 System_PlayerMining이 하고,
    /// 이 표시는 MiningProgressMessage를 받은 대로 막대를 채우고 숨기기만 한다. 그림 에셋 없이 코드로 만든다.
    /// 캐는 동작 애니메이션은 나중에 붙인다.
    /// </summary>
    public sealed class MiningProgressView : MonoBehaviour, IEntityMessageHandler
    {
        private const float CanvasScale = 0.01f;
        private static readonly Vector3 _offset = new(0f, 1.55f, 0f);
        private static readonly Vector2 _size = new(110f, 12f);

        private GameObject? _root;
        private RectTransform? _fill;

        public void RegisterTo(EntityBase entity)
        {
            if (_root == null)
                Build();
            entity.RegisterMessageHandler<MiningProgressMessage>(OnProgress);
        }

        private void OnProgress(MiningProgressMessage message)
        {
            if (_root == null || _fill == null)
                return;
            if (_root.activeSelf != message.Active)
                _root.SetActive(message.Active);
            if (message.Active == true)
                _fill.localScale = new Vector3(Mathf.Clamp01(message.Progress), 1f, 1f);
        }

        private void Build()
        {
            _root = new GameObject("Mining progress bar", typeof(Canvas));
            _root.transform.SetParent(transform, false);
            _root.transform.localPosition = _offset;
            _root.transform.localScale = Vector3.one * CanvasScale;
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 90;
            ((RectTransform)_root.transform).sizeDelta = _size;

            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(_root.transform, false);
            var backgroundRect = background.GetComponent<RectTransform>();
            backgroundRect.sizeDelta = _size;
            var backgroundImage = background.GetComponent<Image>();
            backgroundImage.color = new Color(0f, 0f, 0f, 0.7f);
            backgroundImage.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(background.transform, false);
            _fill = fill.GetComponent<RectTransform>();
            // 왼쪽 끝을 기준으로 가로 배율만 바꿔 채운다
            _fill.anchorMin = new Vector2(0f, 0f);
            _fill.anchorMax = new Vector2(1f, 1f);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.offsetMin = new Vector2(2f, 2f);
            _fill.offsetMax = new Vector2(-2f, -2f);
            _fill.localScale = new Vector3(0f, 1f, 1f);
            var fillImage = fill.GetComponent<Image>();
            fillImage.color = new Color(1f, 0.78f, 0.3f, 1f);
            fillImage.raycastTarget = false;

            _root.SetActive(false);
        }
    }
}
