using System.Collections;
using System.Collections.Generic;
using GameFramework.Event;
using UnityEngine;
using VoxelGameFramework.Core;
using VoxelGameFramework.Events;

namespace VoxelGameFramework.Audio
{
    /// <summary>
    /// 轻量音效管理器 (零外部资产依赖)
    /// 通过程序化合成 (AudioClip.Create) 生成射击/爆炸/通关/UI 音效
    /// 支持音量/静音设置 (SettingsForm 联动)
    ///
    /// 注册进 ServiceLocator (Bootstrap 时或自身 Awake),
    /// 调用方通过 ServiceLocator.Get&lt;VoxelSoundManager&gt;() 获取。
    /// 播放请求走 SfxPlayedEventArgs 命令事件 —— 调用方只 Fire, 本类订阅后执行。
    /// </summary>
    public class VoxelSoundManager : MonoBehaviour
    {
        [Header("音量设置")]
        [Range(0f, 1f)] public float masterVolume = 0.8f;
        [Range(0f, 1f)] public float sfxVolume = 1.0f;
        public bool muted = false;

        [Header("音效源")]
        [SerializeField] private AudioSource _sfxSource;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private bool _initialized = false;

        // 音效类型枚举
        public enum SfxType
        {
            Shoot,        // 射击
            Explosion,    // 体素爆炸
            Click,        // UI 点击
            Win,          // 通关
            SlotPlace,    // 方块入槽
        }

        private void Awake()
        {
            if (_sfxSource == null)
            {
                var go = new GameObject("SfxSource");
                go.transform.SetParent(transform);
                _sfxSource = go.AddComponent<AudioSource>();
                _sfxSource.playOnAwake = false;
                _sfxSource.spatialBlend = 0f; // 2D
            }
        }

        private void Start()
        {
            GenerateAllClips();
            _initialized = true;

            // 注册进服务容器, 并订阅音效命令事件
            ServiceLocator.Register(this);
            // 时序竞态由 GameEventBus 统一兜底 (缓存 → Bootstrap 后 FlushPending), 不会丢失订阅。
            GameEventBus.Subscribe(SfxPlayedEventArgs.EventId, OnSfxRequested);
        }

        private void OnDestroy()
        {
            GameEventBus.Unsubscribe(SfxPlayedEventArgs.EventId, OnSfxRequested);
        }

        /// <summary>
        /// SfxPlayedEventArgs 命令事件的处理器: 解包参数并播放音效。
        /// </summary>
        private void OnSfxRequested(object sender, GameEventArgs e)
        {
            var args = (SfxPlayedEventArgs)e;
            PlaySfx(args.Type, args.VolumeScale);
        }

        /// <summary>
        /// 播放指定类型音效
        /// </summary>
        public void PlaySfx(SfxType type, float volumeScale = 1f)
        {
            if (!_initialized) return;
            if (muted || masterVolume <= 0.01f) return;

            string key = type.ToString();
            if (_clips.TryGetValue(key, out var clip) && clip != null)
            {
                _sfxSource.PlayOneShot(clip, masterVolume * sfxVolume * volumeScale);
            }
        }

        /// <summary>
        /// 设置全局静音
        /// </summary>
        public void SetMuted(bool mute)
        {
            muted = mute;
        }

        /// <summary>
        /// 设置主音量 (0-1)
        /// </summary>
        public void SetMasterVolume(float vol)
        {
            masterVolume = Mathf.Clamp01(vol);
        }

        /// <summary>
        /// 设置音效音量 (0-1)
        /// </summary>
        public void SetSfxVolume(float vol)
        {
            sfxVolume = Mathf.Clamp01(vol);
        }

        #region 程序化音效合成
        private void GenerateAllClips()
        {
            _clips["Shoot"] = GenerateTone(0.09f, 900f, 300f, 0.25f);          // 短促高频射击
            _clips["Explosion"] = GenerateNoise(0.22f, 0.9f, 0.18f);            // 噪声爆炸
            _clips["Click"] = GenerateTone(0.06f, 600f, 800f, 0.2f);           // UI 点击
            _clips["Win"] = GenerateToneSequence(0.6f);                        // 胜利和弦
            _clips["SlotPlace"] = GenerateTone(0.10f, 500f, 200f, 0.3f);       // 入槽
        }

        /// <summary>
        /// 生成单频音调 (带频率滑落)
        /// </summary>
        private AudioClip GenerateTone(float duration, float startFreq, float endFreq, float volume)
        {
            int sampleRate = 44100;
            int samples = (int)(duration * sampleRate);
            float[] data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float progress = (float)i / samples;
                float freq = Mathf.Lerp(startFreq, endFreq, progress);
                float envelope = Mathf.Exp(-progress * 4f); // 指数衰减
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * volume;
            }

            AudioClip clip = AudioClip.Create("Tone", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// 生成白噪声爆发 (爆炸/撞击)
        /// </summary>
        private AudioClip GenerateNoise(float duration, float volume, float decay)
        {
            int sampleRate = 44100;
            int samples = (int)(duration * sampleRate);
            float[] data = new float[samples];

            System.Random rng = new System.Random(12345);
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float envelope = Mathf.Exp(-t * decay * 10f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                data[i] = noise * envelope * volume;
            }

            AudioClip clip = AudioClip.Create("Noise", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// 生成胜利和弦 (C-E-G 琶音)
        /// </summary>
        private AudioClip GenerateToneSequence(float duration)
        {
            int sampleRate = 44100;
            int samples = (int)(duration * sampleRate);
            float[] data = new float[samples];

            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f }; // C5 E5 G5 C6
            float noteDur = duration / notes.Length;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                int noteIdx = Mathf.Min((int)(t / noteDur), notes.Length - 1);
                float noteT = t - noteIdx * noteDur;
                float freq = notes[noteIdx];
                float envelope = Mathf.Exp(-noteT * 2.5f);
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * noteT) * envelope * 0.25f;
            }

            AudioClip clip = AudioClip.Create("Win", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
        #endregion
    }
}
