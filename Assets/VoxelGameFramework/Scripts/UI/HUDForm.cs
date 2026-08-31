using GameFramework;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VoxelGameFramework.Events;
using VoxelGameFramework.Level;

namespace VoxelGameFramework.UI
{
    /// <summary>
    /// HUD 顶部栏表单 (参考图顶部)
    /// 布局: 金币图标+数值(左) | LEVEL 标题(中) | 设置按钮(右)
    /// 纯 C# 运行时构建 (不继承 MonoBehaviour, 由 VoxelUIManager 持有)
    /// </summary>
    public class HUDForm
    {
        private TextMeshProUGUI _coinText;
        private TextMeshProUGUI _levelText;
        private TextMeshProUGUI _progressText;
        private Image _progressFill;
        private VoxelLevelManager _levelManager;
        private System.Action _onSettingsClicked;

        private const float REF_WIDTH = 1080f;
        private const float REF_HEIGHT = 1920f;

        public void Build(RectTransform parent, VoxelLevelManager levelManager, System.Action onSettingsClicked = null)
        {
            _levelManager = levelManager;
            _onSettingsClicked = onSettingsClicked;

            // 表单根节点
            RectTransform root = CreateRect("HUDForm", parent);
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.sizeDelta = new Vector2(0f, 200f);
            root.anchoredPosition = Vector2.zero;

            // 顶部栏背景 (半透明黑条)
            Image barBg = CreateImage("TopBar", root, new Color(0f, 0f, 0f, 0.35f));
            barBg.rectTransform.anchorMin = new Vector2(0f, 0f);
            barBg.rectTransform.anchorMax = new Vector2(1f, 1f);
            barBg.rectTransform.offsetMin = Vector2.zero;
            barBg.rectTransform.offsetMax = Vector2.zero;

            // 左上: 金币图标 + 数值
            Image coinIcon = CreateImage("CoinIcon", root, new Color(1f, 0.84f, 0f, 1f));
            coinIcon.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            coinIcon.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            coinIcon.rectTransform.pivot = new Vector2(0f, 0.5f);
            coinIcon.rectTransform.sizeDelta = new Vector2(70f, 70f);
            coinIcon.rectTransform.anchoredPosition = new Vector2(40f, 0f);
            coinIcon.rectTransform.localRotation = Quaternion.Euler(0, 0, 15f);

            _coinText = CreateText("CoinText", root, "1000", 44f, Color.white, TextAlignmentOptions.Center);
            _coinText.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            _coinText.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            _coinText.rectTransform.pivot = new Vector2(0f, 0.5f);
            _coinText.rectTransform.sizeDelta = new Vector2(260f, 80f);
            _coinText.rectTransform.anchoredPosition = new Vector2(120f, 0f);
            _coinText.alignment = TextAlignmentOptions.Left;
            _coinText.fontStyle = FontStyles.Bold;

            // 顶部中央: LEVEL 标题
            _levelText = CreateText("LevelTitle", root, "LEVEL 1", 52f, Color.white, TextAlignmentOptions.Center);
            _levelText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _levelText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _levelText.rectTransform.sizeDelta = new Vector2(500f, 90f);
            _levelText.fontStyle = FontStyles.Bold;

            // 右上: 设置按钮 (齿轮)
            Button settingsBtn = CreateButton("SettingsButton", root, new Color(0.2f, 0.45f, 0.95f, 1f));
            settingsBtn.GetComponent<RectTransform>().anchorMin = new Vector2(1f, 0.5f);
            settingsBtn.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 0.5f);
            settingsBtn.GetComponent<RectTransform>().pivot = new Vector2(1f, 0.5f);
            settingsBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 80f);
            settingsBtn.GetComponent<RectTransform>().anchoredPosition = new Vector2(-40f, 0f);
            settingsBtn.onClick.AddListener(() => _onSettingsClicked?.Invoke());

            TextMeshProUGUI gearText = CreateText("Gear", settingsBtn.transform as RectTransform, "设置", 32f, Color.white, TextAlignmentOptions.Center);
            StretchFull(gearText.rectTransform);

            // 进度条 (中部下方)
            RectTransform progressArea = CreateRect("ProgressArea", root);
            progressArea.anchorMin = new Vector2(0f, 0f);
            progressArea.anchorMax = new Vector2(1f, 0f);
            progressArea.pivot = new Vector2(0.5f, 0f);
            progressArea.sizeDelta = new Vector2(0f, 70f);
            progressArea.anchoredPosition = new Vector2(0f, 20f);

            _progressText = CreateText("ProgressText", progressArea, "粉碎进度: 0%", 32f, new Color(1f, 0.95f, 0.7f), TextAlignmentOptions.Left);
            _progressText.rectTransform.anchorMin = new Vector2(0.05f, 1f);
            _progressText.rectTransform.anchorMax = new Vector2(0.95f, 1f);
            _progressText.rectTransform.pivot = new Vector2(0.5f, 1f);
            _progressText.rectTransform.sizeDelta = new Vector2(0f, 34f);
            _progressText.rectTransform.anchoredPosition = Vector2.zero;

            // 进度条背景 + 填充
            Image barBg2 = CreateImage("BarBg", progressArea, new Color(0f, 0f, 0f, 0.5f));
            barBg2.rectTransform.anchorMin = new Vector2(0.05f, 0f);
            barBg2.rectTransform.anchorMax = new Vector2(0.95f, 0f);
            barBg2.rectTransform.pivot = new Vector2(0.5f, 0f);
            barBg2.rectTransform.sizeDelta = new Vector2(0f, 22f);
            barBg2.rectTransform.anchoredPosition = Vector2.zero;

            _progressFill = CreateImage("BarFill", progressArea, new Color(0.95f, 0.25f, 0.65f, 1f));
            _progressFill.rectTransform.anchorMin = new Vector2(0.05f, 0f);
            _progressFill.rectTransform.anchorMax = new Vector2(0.05f, 0f);
            _progressFill.rectTransform.pivot = new Vector2(0f, 0f);
            _progressFill.rectTransform.sizeDelta = new Vector2(0f, 22f);
            _progressFill.rectTransform.anchoredPosition = Vector2.zero;
        }

        // 脏检查缓存: TMP 赋相同字符串仍会触发排版重建, 逐帧无脑赋值是纯浪费
        private int _lastCoins = -1;
        private int _lastLevelIndex = -1;
        private float _lastRatio = -1f;

        /// <summary>
        /// 刷新金币/关卡标题等低频信息。内部做脏检查, 值不变时不触碰 TMP。
        /// </summary>
        public void RefreshStaticInfo()
        {
            if (_levelManager == null) return;

            if (_levelManager.totalCoins != _lastCoins)
            {
                _lastCoins = _levelManager.totalCoins;
                _coinText.text = _lastCoins.ToString("N0");
            }

            if (_levelManager.currentLevelIndex != _lastLevelIndex)
            {
                _lastLevelIndex = _levelManager.currentLevelIndex;
                _levelText.text = $"LEVEL {_lastLevelIndex + 1}";
            }
        }

        /// <summary>
        /// 由 DestructionProgressEventArgs 事件推送的破坏进度, 不再逐帧从模型拉取。
        /// </summary>
        public void SetProgress(float ratio)
        {
            ratio = Mathf.Clamp01(ratio);

            // 进度条按 0.1% 粒度更新就足够顺滑, 避免每帧重建
            if (Mathf.Abs(ratio - _lastRatio) < 0.001f) return;
            _lastRatio = ratio;

            _progressText.text = $"粉碎进度: {ratio * 100f:F1}%";
            _progressFill.rectTransform.anchorMax = new Vector2(0.05f + 0.9f * ratio, 0f);
        }

        /// <summary>
        /// 换关时清空脏检查缓存, 强制下一帧全量刷新
        /// </summary>
        public void InvalidateCache()
        {
            _lastCoins = -1;
            _lastLevelIndex = -1;
            _lastRatio = -1f;
        }

        #region UI 构建工具
        private RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            return tmp;
        }

        private Button CreateButton(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go.GetComponent<Button>();
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
