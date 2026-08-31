using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VoxelGameFramework.Audio;
using VoxelGameFramework.Core;
using VoxelGameFramework.Events;
using VoxelGameFramework.Level;

namespace VoxelGameFramework.UI
{
    /// <summary>
    /// 设置面板表单
    /// 内容: 主音量滑块 | 音效音量滑块 | 静音开关 | 重启关卡 | 返回
    /// </summary>
    public class SettingsForm
    {
        private GameObject _root;
        private VoxelSoundManager _soundManager;
        private VoxelLevelManager _levelManager;
        private Slider _masterSlider;
        private Slider _sfxSlider;
        private Toggle _muteToggle;

        public bool IsOpen => _root != null && _root.activeSelf;

        /// <summary>
        /// 构建设置面板 (初始隐藏)
        /// </summary>
        public void Build(RectTransform parent, VoxelSoundManager soundManager, VoxelLevelManager levelManager)
        {
            _soundManager = soundManager;
            _levelManager = levelManager;

            // 全屏遮罩
            var maskGo = new GameObject("SettingsForm", typeof(RectTransform), typeof(Image));
            maskGo.transform.SetParent(parent, false);
            var mask = maskGo.GetComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.6f);
            mask.raycastTarget = true;
            var maskRt = mask.rectTransform;
            maskRt.anchorMin = Vector2.zero;
            maskRt.anchorMax = Vector2.one;
            maskRt.offsetMin = Vector2.zero;
            maskRt.offsetMax = Vector2.zero;
            _root = maskGo;

            // 面板
            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(maskRt, false);
            var panelImg = panelGo.GetComponent<Image>();
            panelImg.color = new Color(0.1f, 0.14f, 0.22f, 0.96f);
            var panelRt = panelImg.rectTransform;
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(720f, 640f);

            // 标题
            CreateText("Title", panelRt, "设置", 48f, new Color(1f, 0.9f, 0.5f),
                new Vector2(0f, 0.85f), new Vector2(1f, 0.98f));

            // 主音量
            CreateLabel("MasterLabel", panelRt, "主音量", 34f,
                new Vector2(0.1f, 0.68f), new Vector2(0.4f, 0.75f), TextAlignmentOptions.Left);
            _masterSlider = CreateSlider("MasterSlider", panelRt, 0.8f,
                new Vector2(0.5f, 0.66f), new Vector2(0.9f, 0.76f), OnMasterChanged);

            // 音效音量
            CreateLabel("SfxLabel", panelRt, "音效音量", 34f,
                new Vector2(0.1f, 0.52f), new Vector2(0.4f, 0.59f), TextAlignmentOptions.Left);
            _sfxSlider = CreateSlider("SfxSlider", panelRt, 1.0f,
                new Vector2(0.5f, 0.50f), new Vector2(0.9f, 0.60f), OnSfxChanged);

            // 静音开关
            CreateLabel("MuteLabel", panelRt, "静音", 34f,
                new Vector2(0.1f, 0.36f), new Vector2(0.4f, 0.43f), TextAlignmentOptions.Left);
            _muteToggle = CreateToggle("MuteToggle", panelRt, false,
                new Vector2(0.5f, 0.34f), new Vector2(0.9f, 0.44f), OnMuteChanged);

            // 重启关卡按钮
            CreateButton("RestartButton", panelRt, "重启关卡", new Color(0.9f, 0.5f, 0.2f),
                new Vector2(0.15f, 0.12f), new Vector2(0.55f, 0.24f), () =>
                {
                    Close();

                    // 走事件驱动: 由 ProcedureGameplay 订阅并重新装配关卡。
                    // 没有 GameFramework 时降级为直接调用, 保证独立场景仍可用。
                    if (GameEventBus.IsAvailable)
                    {
                        GameEventBus.Fire(_levelManager, RestartLevelRequestedEventArgs.Create());
                    }
                    else
                    {
                        _levelManager?.RestartCurrentLevel();
                    }
                });

            // 返回按钮
            CreateButton("CloseButton", panelRt, "返回", new Color(0.3f, 0.45f, 0.8f),
                new Vector2(0.6f, 0.12f), new Vector2(0.9f, 0.24f), Close);

            // 初始隐藏
            _root.SetActive(false);
        }

        public void Open()
        {
            if (_root != null)
            {
                _root.SetActive(true);

                // 同步当前音量设置
                if (_soundManager != null)
                {
                    _masterSlider.value = _soundManager.masterVolume;
                    _sfxSlider.value = _soundManager.sfxVolume;
                    _muteToggle.isOn = _soundManager.muted;
                }
            }
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);
        }

        #region 事件处理
        private void OnMasterChanged(float val)
        {
            _soundManager?.SetMasterVolume(val);
        }

        private void OnSfxChanged(float val)
        {
            _soundManager?.SetSfxVolume(val);
        }

        private void OnMuteChanged(bool mute)
        {
            _soundManager?.SetMuted(mute);
        }
        #endregion

        #region UI 工具
        private void CreateText(string name, Transform parent, string text, float fontSize, Color color,
            Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            var rt = tmp.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void CreateLabel(string name, Transform parent, string text, float fontSize,
            Vector2 anchorMin, Vector2 anchorMax, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            var rt = tmp.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private Slider CreateSlider(string name, Transform parent, float value,
            Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction<float> onChanged)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            var slider = go.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = value;
            slider.onValueChanged.AddListener(onChanged);
            var rt = (RectTransform)slider.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // 背景条
            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(rt, false);
            bg.GetComponent<Image>().color = new Color(0.2f, 0.25f, 0.35f, 1f);
            var bgRt = bg.GetComponent<RectTransform>();
            StretchFull(bgRt);

            // 填充
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(rt, false);
            fill.GetComponent<Image>().color = new Color(0.35f, 0.75f, 1f, 1f);
            var fillRt = fill.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(slider.value, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            slider.fillRect = fillRt;

            // 手柄
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(rt, false);
            handle.GetComponent<Image>().color = Color.white;
            var handleRt = handle.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(24f, 24f);
            slider.handleRect = handleRt;
            slider.targetGraphic = handle.GetComponent<Image>();

            return slider;
        }

        private Toggle CreateToggle(string name, Transform parent, bool value,
            Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction<bool> onChanged)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(parent, false);
            var toggle = go.GetComponent<Toggle>();
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(onChanged);
            var rt = (RectTransform)toggle.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(rt, false);
            bg.GetComponent<Image>().color = new Color(0.25f, 0.3f, 0.4f, 1f);
            var bgRt = bg.GetComponent<RectTransform>();
            StretchFull(bgRt);
            toggle.targetGraphic = bg.GetComponent<Image>();
            toggle.graphic = bg.GetComponent<Image>();

            return toggle;
        }

        private void CreateButton(string name, Transform parent, string label, Color color,
            Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(onClick);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(rt, false);
            var tmp = text.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 34f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            var textRt = tmp.rectTransform;
            StretchFull(textRt);
        }

        private void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
        #endregion
    }
}
