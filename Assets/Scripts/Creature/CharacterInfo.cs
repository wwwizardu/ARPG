#nullable enable
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D.Animation;
using UnityEditor.Animations;

namespace ARPG.Creature
{
    public class CharacterInfo : MonoBehaviour
    {
        [SerializeField] protected Sprite _characterSprite;
        [SerializeField] protected SpriteRenderer _sr;
        [SerializeField] protected Animator _animator;
        [SerializeField] protected Image _hpBar;
        [SerializeField] protected TMPro.TextMeshPro _textName;
        [SerializeField] protected Transform _buffImageRoot;
        [SerializeField] protected BoxCollider2D _boxCollider;

        public Sprite CharacterSprite => _characterSprite;
        public SpriteRenderer Sr => _sr;
        public Animator Animator => _animator;
        public Image HpBar => _hpBar;
        
        public TMPro.TextMeshPro TextName => _textName;

        public Transform BuffIconRoot => _buffImageRoot;

        public BoxCollider2D BoxCollider => _boxCollider;

        public SpriteLibrary? SpriteLibrary { get; private set; }

        private float _attackTime = -1;

        public void Initialize(CharacterBase inCharacter)
        {
            SpriteLibrary = _sr.GetComponent<SpriteLibrary>();

            AnimationClip? clip = GetAnimationClip("Attack");
            if (clip != null)
            {
                _attackTime = GetEventTime(clip, "OnAttackAnimationEvent");
            }
        }

        public void OnAttackAnimationEvent()
        {

        }
        public float GetAttackEventTime()
        {
            return _attackTime;
        }

        private AnimationClip? GetAnimationClip(string clipName)
        {
            if (_animator == null)
                return null;

            RuntimeAnimatorController ac = _animator.runtimeAnimatorController;

            for (int i = 0; i < ac.animationClips.Length; i++)
            {
                if (ac.animationClips[i].name == clipName)
                {
                    return ac.animationClips[i];
                }
            }

            return null;
        }
        
        private float GetEventTime(AnimationClip clip, string eventName)
        {
            AnimationEvent[] events = clip.events;

            for (int i = 0; i < events.Length; i++)
            {
                if (events[i].functionName == eventName)
                {
                    return events[i].time;
                }
            }

            Debug.LogError($"[CharacterInfo] GetEventTime() - cannot found event, Name({eventName})");
            return -1f; // 이벤트를 찾지 못한 경우
        }
    }
}