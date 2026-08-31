using GameFramework;
using GameFramework.Event;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VoxelGameFramework.Events;

namespace VoxelGameFramework.UI
{
    /// <summary>
    /// 通关弹窗表单 (居中模态)
    /// 内容: 胜利标题 | 奖励金币 | 下一关按钮
    /// 纯 C# 运行时构建
    /// </summary>
    public class VictoryForm
    {
        private TextMeshProUGUI _rewardText;
        private Button _nextButton;
        private System.Action _onNextClicked;

        public void Build(RectTransform parent, System.Action onNextClicked)
        {
            _onNextClicked = onNextClicked;

            // 全屏遮罩 (点击不穿透)
            var maskGo = new GameObject("VictoryForm", typeof(RectTransform), typeof(Image));
            maskGo.transform.SetParent(parent, false);
            var mask = maskGo.GetComponent<Image>();
            mask.color = new Color(0f, 0f, 0f, 0.65f);
            mask.raycastTarget = true;
            var maskRt = mask.rectTransform;
            maskRt.anchorMin = Vector2.zero;
            maskRt.anchorMax = Vector2.one;
            maskRt.offsetMin = Vector2.zero;
            maskRt.offsetMax = Vector2.zero;

            // 弹窗面板
            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(maskRt, false);
            var panelImg = panelGo.GetComponent<Image>();
            panelImg.color = new Color(0.1f, 0.14f, 0.22f, 0.95f);
            panelImg.raycastTarget = true;
            var panelRt = panelImg.rectTransform;
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(760f, 560f);

            // 标题
            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(panelRt, false);
            var title = titleGo.GetComponent<TextMeshProUGUI>();
            title.text = "关卡大获全胜！";
            title.fontSize = 56f;
            title.fontStyle = FontStyles.Bold;
            title.color = new Color(1f, 0.85f, 0.3f, 1f);
            title.alignment = TextAlignmentOptions.Center;
            title.raycastTarget = false;
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 0.7f);
            titleRt.anchorMax = new Vector2(1f, 0.9f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;

            // 奖励文本
            var rewardGo = new GameObject("Reward", typeof(RectTransform), typeof(TextMeshProUGUI));
            rewardGo.transform.SetParent(panelRt, false);
            _rewardText = rewardGo.GetComponent<TextMeshProUGUI>();
            _rewardText.text = "获得通关金币: +500";
            _rewardText.fontSize = 42f;
            _rewardText.color = Color.white;
            _rewardText.alignment = TextAlignmentOptions.Center;
            _rewardText.raycastTarget = false;
            var rewardRt = _rewardText.rectTransform;
            rewardRt.anchorMin = new Vector2(0f, 0.45f);
            rewardRt.anchorMax = new Vector2(1f, 0.65f);
            rewardRt.offsetMin = Vector2.zero;
            rewardRt.offsetMax = Vector2.zero;

            // 下一关按钮
            var btnGo = new GameObject("NextButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(panelRt, false);
            var btnImg = btnGo.GetComponent<Image>();
            btnImg.color = new Color(0.2f, 0.85f, 0.45f, 1f);
            _nextButton = btnGo.GetComponent<Button>();
            _nextButton.onClick.AddListener(() => _onNextClicked?.Invoke());
            var btnRt = btnImg.rectTransform;
            btnRt.anchorMin = new Vector2(0.2f, 0.08f);
            btnRt.anchorMax = new Vector2(0.8f, 0.28f);
            btnRt.offsetMin = Vector2.zero;
            btnRt.offsetMax = Vector2.zero;

            var btnTextGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            btnTextGo.transform.SetParent(btnRt, false);
            var btnText = btnTextGo.GetComponent<TextMeshProUGUI>();
            btnText.text = "进入下一关";
            btnText.fontSize = 40f;
            btnText.fontStyle = FontStyles.Bold;
            btnText.color = Color.white;
            btnText.alignment = TextAlignmentOptions.Center;
            btnText.raycastTarget = false;
            var btnTextRt = btnText.rectTransform;
            btnTextRt.anchorMin = Vector2.zero;
            btnTextRt.anchorMax = Vector2.one;
            btnTextRt.offsetMin = Vector2.zero;
            btnTextRt.offsetMax = Vector2.zero;
        }

        public void SetReward(int coins)
        {
            if (_rewardText != null)
            {
                _rewardText.text = $"获得通关金币: +{coins}";
            }
        }
    }
}
