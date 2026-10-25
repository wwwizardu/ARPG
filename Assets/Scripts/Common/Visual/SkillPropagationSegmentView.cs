using UnityEngine;

namespace ARPG.Visual
{
    /// <summary>
    /// Optional view contract for a pooled instant-propagation VFX prefab. The gameplay
    /// scheduler passes snapshots; the view never reads or changes ECS state.
    /// </summary>
    public class SkillPropagationSegmentView : MonoBehaviour
    {
        [SerializeField] private Transform _effectRoot = null!;
        [SerializeField, Min(0.01f)] private float _referenceLength = 10.73f;
        [SerializeField, Min(0.01f)] private float _widthScale = 0.55f;

        public void SetEndpoints(Vector2 inStartPosition, Vector2 inEndPosition)
        {
            if (_effectRoot == null)
                return;

            Vector2 targetToSource = inStartPosition - inEndPosition;
            float distance = targetToSource.magnitude;
            float angle = distance > Mathf.Epsilon
                ? Vector2.SignedAngle(Vector2.up, targetToSource / distance)
                : 0f;

            transform.position = new Vector3(inEndPosition.x, inEndPosition.y, 0f);
            _effectRoot.localPosition = Vector3.zero;
            _effectRoot.localRotation = Quaternion.AngleAxis(angle, Vector3.forward);
            _effectRoot.localScale = new Vector3(
                _widthScale,
                Mathf.Max(distance, 0.01f) / _referenceLength,
                _widthScale);
        }
    }
}
