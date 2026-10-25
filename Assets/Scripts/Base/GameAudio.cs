#nullable enable
using UnityEngine;

namespace ARPG.Input
{
    public enum GameAudioChannel { Music, Effects, UI }

    /// <summary>UIManager owns the three audio outputs and their lifecycle.</summary>
    public sealed class GameAudio
    {
        private readonly AudioSource[] _sources = new AudioSource[3];
        private AudioClip? _sample;
        public void Initialize(GameObject owner)
        {
            for (int i = 0; i < _sources.Length; i++)
            {
                _sources[i] = owner.AddComponent<AudioSource>();
                _sources[i].playOnAwake = false;
                _sources[i].loop = i == 0;
            }
        }
        public void SetVolumes(float music, float effects, float ui)
        {
            if (_sources[0] == null) return;
            _sources[0].volume = music; _sources[1].volume = effects; _sources[2].volume = ui;
        }
        public void Preview(GameAudioChannel channel)
        {
            if (_sample == null)
            {
                const int sampleRate = 22050;
                float[] samples = new float[sampleRate / 4];
                for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Sin(i * 660f * Mathf.PI * 2f / sampleRate) * 0.15f * (1f - i / (float)samples.Length);
                _sample = AudioClip.Create("SettingsPreview", samples.Length, 1, sampleRate, false); _sample.SetData(samples, 0);
            }
            _sources[(int)channel].PlayOneShot(_sample);
        }
        public void Play(AudioClip clip, GameAudioChannel channel)
        {
            if (clip == null || _sources[(int)channel] == null) return;
            if (channel == GameAudioChannel.Music) { _sources[0].clip = clip; _sources[0].Play(); }
            else _sources[(int)channel].PlayOneShot(clip);
        }
        public void Reset() { for (int i = 0; i < _sources.Length; i++) if (_sources[i] != null) _sources[i].Stop(); }
        public void Dispose() { Reset(); if (_sample != null) Object.Destroy(_sample); }
    }
}
