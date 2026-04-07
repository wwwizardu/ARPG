#nullable enable

using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace ARPG.UI
{
    public class FloatingTextManager : MonoBehaviour
    {
        private const int POOL_SIZE = 20;
        private const float FLOAT_DISTANCE = 1.0f;
        private const float ANIMATION_DURATION = 0.8f;
        private const float FADE_START_RATIO = 0.5f;
        private const float CRITICAL_SCALE_MULTIPLIER = 1.5f;
        private const float PUNCH_SCALE_STRENGTH = 0.3f;
        private const float PUNCH_DURATION = 0.15f;
        private const float RANDOM_X_OFFSET = 0.3f;
        private const float CANVAS_SCALE = 0.01f;
        private const float Y_OFFSET = 0.5f;

        private struct FloatingTextItem
        {
            public GameObject Root;
            public TextMeshProUGUI Text;
            public CanvasGroup CanvasGroup;
            public RectTransform RectTransform;
        }

        private Canvas? _canvas;
        private List<FloatingTextItem> _pool = new List<FloatingTextItem>();
        private int _nextIndex;

        public void Initialize()
        {
            GameObject canvasGo = new GameObject("FloatingTextCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 100;
            canvasGo.transform.localScale = Vector3.one * CANVAS_SCALE;

            _pool = new List<FloatingTextItem>(POOL_SIZE);
            _nextIndex = 0;

            for (int i = 0; i < POOL_SIZE; i++)
            {
                FloatingTextItem item = CreateTextItem(i);
                item.Root.SetActive(false);
                _pool.Add(item);
            }
        }

        public void Reset()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                DOTween.Kill(_pool[i].Root.GetInstanceID());
                _pool[i].Root.SetActive(false);
                _pool[i].CanvasGroup.alpha = 1f;
            }

            _nextIndex = 0;
        }

        public void ShowDamageText(
            Vector3 worldPosition,
            int damageAmount,
            GlobalEnum.DamageType damageType,
            bool isCritical,
            bool isEvaded,
            bool isBlocked)
        {
            if (_pool.Count == 0)
                return;

            FloatingTextItem item = _pool[_nextIndex];
            _nextIndex = (_nextIndex + 1) % _pool.Count;

            DOTween.Kill(item.Root.GetInstanceID());

            SetupTextContent(item, damageAmount, damageType, isCritical, isEvaded, isBlocked);

            float randomX = Random.Range(-RANDOM_X_OFFSET, RANDOM_X_OFFSET);
            Vector3 localPos = new Vector3(
                (worldPosition.x + randomX) / CANVAS_SCALE,
                (worldPosition.y + Y_OFFSET) / CANVAS_SCALE,
                0f
            );
            item.RectTransform.localPosition = localPos;

            item.CanvasGroup.alpha = 1f;
            float baseScale = isCritical ? CRITICAL_SCALE_MULTIPLIER : 1f;
            item.Root.transform.localScale = Vector3.one * baseScale;
            item.Root.SetActive(true);

            int tweenId = item.Root.GetInstanceID();
            float duration = isEvaded ? 0.6f : ANIMATION_DURATION;
            float floatDist = isEvaded ? 0.5f : FLOAT_DISTANCE;

            Sequence seq = DOTween.Sequence();
            seq.SetId(tweenId);

            // 펀치 스케일 (타격감)
            if (isEvaded == false)
            {
                float punchStrength = isCritical
                    ? PUNCH_SCALE_STRENGTH * CRITICAL_SCALE_MULTIPLIER
                    : PUNCH_SCALE_STRENGTH;

                seq.Append(
                    item.Root.transform.DOPunchScale(
                        Vector3.one * punchStrength,
                        PUNCH_DURATION,
                        vibrato: 1,
                        elasticity: 0.5f
                    )
                );
            }

            // 위로 떠오름
            float floatEndY = localPos.y + (floatDist / CANVAS_SCALE);
            seq.Join(
                item.RectTransform.DOLocalMoveY(floatEndY, duration)
                    .SetEase(Ease.OutQuad)
            );

            // 페이드 아웃
            float fadeDelay = duration * FADE_START_RATIO;
            float fadeDuration = duration * (1f - FADE_START_RATIO);
            seq.Insert(fadeDelay,
                DOTween.To(
                    () => item.CanvasGroup.alpha,
                    x => item.CanvasGroup.alpha = x,
                    0f,
                    fadeDuration
                ).SetEase(Ease.InQuad)
            );

            seq.OnComplete(() =>
            {
                item.Root.SetActive(false);
            });
        }

        private FloatingTextItem CreateTextItem(int index)
        {
            GameObject root = new GameObject($"DmgText_{index}");
            root.transform.SetParent(_canvas!.transform, false);

            RectTransform rt = root.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(200f, 50f);

            CanvasGroup cg = root.AddComponent<CanvasGroup>();

            TextMeshProUGUI tmp = root.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 36f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;

            return new FloatingTextItem
            {
                Root = root,
                Text = tmp,
                CanvasGroup = cg,
                RectTransform = rt
            };
        }

        private void SetupTextContent(
            FloatingTextItem item,
            int damageAmount,
            GlobalEnum.DamageType damageType,
            bool isCritical,
            bool isEvaded,
            bool isBlocked)
        {
            if (isEvaded)
            {
                item.Text.text = "MISS";
                item.Text.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                item.Text.fontSize = 28f;
                return;
            }

            if (isBlocked)
            {
                item.Text.text = $"{damageAmount}\n<size=60%>BLOCK</size>";
                item.Text.color = new Color(0.6f, 0.8f, 1f, 1f);
                item.Text.fontSize = 30f;
                return;
            }

            item.Text.text = damageAmount.ToString();
            item.Text.fontSize = isCritical ? 44f : 36f;
            item.Text.color = GetDamageColor(damageType, isCritical);
        }

        private Color GetDamageColor(GlobalEnum.DamageType damageType, bool isCritical)
        {
            if (isCritical)
            {
                return new Color(1.0f, 0.85f, 0.0f, 1f); // 골드
            }

            switch (damageType)
            {
                case GlobalEnum.DamageType.Physics:
                    return new Color(1.0f, 1.0f, 1.0f, 1f);   // 흰색
                case GlobalEnum.DamageType.Fire:
                    return new Color(1.0f, 0.4f, 0.1f, 1f);   // 주황빨강
                case GlobalEnum.DamageType.Ice:
                    return new Color(0.3f, 0.7f, 1.0f, 1f);   // 하늘색
                case GlobalEnum.DamageType.Lightning:
                    return new Color(1.0f, 1.0f, 0.3f, 1f);   // 노란색
                case GlobalEnum.DamageType.Poison:
                    return new Color(0.4f, 1.0f, 0.2f, 1f);   // 녹색
                default:
                    return Color.white;
            }
        }
    }
}
