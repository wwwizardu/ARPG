using UnityEngine;

namespace ARPG.View
{
    /// <summary>
    /// 플레이어 수명에 귀속된 조준 방향 화살표 표시만 담당합니다.
    /// </summary>
    public class SkillAimIndicatorView : MonoBehaviour
    {
        [SerializeField] private Transform _rotationRoot;
        [SerializeField] private SpriteRenderer _imageRenderer;
        [SerializeField] private float _angleOffsetDeg;

        public float AngleOffsetDeg => _angleOffsetDeg;

        public void Show()
        {
            if (_imageRenderer != null)
            {
                _imageRenderer.enabled = true;
            }
        }

        public void Hide()
        {
            if (_imageRenderer != null)
            {
                _imageRenderer.enabled = false;
            }
        }

        public void SetAngle(float angleDeg)
        {
            if (_rotationRoot != null)
            {
                _rotationRoot.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
            }
        }

        public void ResetView()
        {
            Hide();
            if (_rotationRoot != null)
            {
                _rotationRoot.localRotation = Quaternion.identity;
            }
        }

        private void Awake()
        {
            ResetView();
        }
    }
}
