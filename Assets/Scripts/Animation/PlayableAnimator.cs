using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using System.Collections.Generic;

namespace ARPG
{
    public class PlayableAnimator : MonoBehaviour
    {
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private Animator _animator;

        private Dictionary<int, int> _clipHashToIndex = new Dictionary<int, int>();
        private List<AnimationClipPlayable> _clipPlayables = new List<AnimationClipPlayable>();

        private int _currentIndex = -1;
        private int _targetIndex = -1;
        private float _blendTime;
        private float _blendProgress = 1f;

        public bool IsInitialized => _graph.IsValid();

        public void Initialize(AnimationClip[] clips)
        {
            _animator = GetComponent<Animator>();
            if (_animator == null)
            {
                _animator = gameObject.AddComponent<Animator>();
            }

            _graph = PlayableGraph.Create($"PlayableGraph_{gameObject.name}");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            var output = AnimationPlayableOutput.Create(_graph, "Animation", _animator);

            _mixer = AnimationMixerPlayable.Create(_graph, clips.Length);
            output.SetSourcePlayable(_mixer);

            for (int i = 0; i < clips.Length; i++)
            {
                var clipPlayable = AnimationClipPlayable.Create(_graph, clips[i]);
                _graph.Connect(clipPlayable, 0, _mixer, i);
                _mixer.SetInputWeight(i, 0f);

                int hash = Animator.StringToHash(clips[i].name);
                _clipHashToIndex[hash] = i;
                _clipPlayables.Add(clipPlayable);
            }

            _graph.Play();
        }

        public void Play(int animationHash, bool isForce = false, float blendTime = 0f)
        {
            if (_clipHashToIndex.TryGetValue(animationHash, out int index) == false)
            {
                return;
            }

            if (isForce == false && _currentIndex == index)
            {
                return;
            }

            _targetIndex = index;
            _blendTime = blendTime;

            _clipPlayables[index].SetTime(0);

            if (_blendTime <= 0f)
            {
                if (_currentIndex >= 0)
                {
                    _mixer.SetInputWeight(_currentIndex, 0f);
                }
                _mixer.SetInputWeight(_targetIndex, 1f);
                _currentIndex = _targetIndex;
                _blendProgress = 1f;
            }
            else
            {
                _blendProgress = 0f;
            }
        }

        private void Update()
        {
            UpdateBlend(Time.deltaTime);
        }

        private void UpdateBlend(float deltaTime)
        {
            if (_blendProgress >= 1f)
            {
                return;
            }

            _blendProgress += deltaTime / _blendTime;
            _blendProgress = Mathf.Min(_blendProgress, 1f);

            float t = _blendProgress;

            if (_currentIndex >= 0)
            {
                _mixer.SetInputWeight(_currentIndex, 1f - t);
            }

            if (_targetIndex >= 0)
            {
                _mixer.SetInputWeight(_targetIndex, t);
            }

            if (_blendProgress >= 1f)
            {
                if (_currentIndex >= 0)
                {
                    _mixer.SetInputWeight(_currentIndex, 0f);
                }
                _currentIndex = _targetIndex;
            }
        }

        public void Reset()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            _clipHashToIndex.Clear();
            _clipPlayables.Clear();
            _currentIndex = -1;
            _targetIndex = -1;
            _blendProgress = 1f;
        }

        private void OnDestroy()
        {
            Reset();
        }
    }
}
