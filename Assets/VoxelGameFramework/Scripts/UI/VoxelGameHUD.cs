using UnityEngine;
using VoxelGameFramework.Core;
using VoxelGameFramework.Level;

namespace VoxelGameFramework.UI
{
    /// <summary>
    /// 独立游戏 HUD 界面控制器（自适应 1080x1920 竖屏与各分辨率，底部零遮挡）
    /// </summary>
    public class VoxelGameHUD : MonoBehaviour
    {
        private float _currentProgress = 0f;
        private int _activeVoxels = 0;
        private int _totalVoxels = 0;
        private float _fps = 60f;
        private float _fpsTimer = 0f;

        private void OnEnable()
        {
            VoxelGameEvents.OnDestructionProgressChanged += HandleProgressChanged;
        }

        private void OnDisable()
        {
            VoxelGameEvents.OnDestructionProgressChanged -= HandleProgressChanged;
        }

        private void HandleProgressChanged(float progress, int active, int total)
        {
            _currentProgress = Mathf.Clamp01(progress);
            _activeVoxels = active;
            _totalVoxels = total;
        }

        private void Update()
        {
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer > 0.35f)
            {
                _fps = 1.0f / Mathf.Max(1e-4f, Time.unscaledDeltaTime);
                _fpsTimer = 0f;
            }
        }

        private void OnGUI()
        {
            VoxelLevelManager lm = VoxelLevelManager.Instance;
            if (lm == null) return;

            float screenW = Screen.width;
            float screenH = Screen.height;

            // 自适应缩放比例
            float scale = Mathf.Clamp(screenW / 540f, 0.9f, 2.2f);
            int baseFontSize = Mathf.RoundToInt(13 * scale);
            int titleFontSize = Mathf.RoundToInt(16 * scale);

            // 1. 顶部游戏状态主面板 (适配 9:16 竖屏)
            float cardW = Mathf.Min(screenW * 0.92f, 520f * scale);
            float cardH = 110f * scale;
            float cardX = (screenW - cardW) * 0.5f;

            GUILayout.BeginArea(new Rect(cardX, 20f * scale, cardW, cardH));
            GUILayout.BeginVertical("box");

            // 关卡名与金币
            GUILayout.BeginHorizontal();
            string lvlName = (lm.levelPlaylists.Count > lm.currentLevelIndex) ? lm.levelPlaylists[lm.currentLevelIndex].levelTitle : "关卡进行中";
            GUILayout.Label($"🏆 <b>{lvlName}</b>", new GUIStyle(GUI.skin.label) { fontSize = titleFontSize, richText = true });
            GUILayout.FlexibleSpace();
            GUILayout.Label($"💰 <b>{lm.totalCoins:N0}</b>  |  ⚡ {Mathf.RoundToInt(_fps)} FPS", new GUIStyle(GUI.skin.label) { fontSize = baseFontSize, richText = true });
            GUILayout.EndHorizontal();

            GUILayout.Space(2 * scale);

            // 破坏百分比进度
            GUILayout.BeginHorizontal();
            GUILayout.Label($"🔥 粉碎进度: <b>{(_currentProgress * 100f):F1}%</b> (余: {_activeVoxels:N0})", new GUIStyle(GUI.skin.label) { fontSize = baseFontSize, richText = true });
            GUILayout.FlexibleSpace();

            // 顶部迷你功能按钮 (重置/跳关，不遮挡底部排队队列)
            if (GUILayout.Button("↺", GUILayout.Width(28 * scale), GUILayout.Height(22 * scale)))
            {
                lm.RestartCurrentLevel();
            }
            if (GUILayout.Button("⏭", GUILayout.Width(28 * scale), GUILayout.Height(22 * scale)))
            {
                lm.NextLevel();
            }
            GUILayout.EndHorizontal();

            // 鲜亮进度条
            Rect barRect = GUILayoutUtility.GetRect(cardW - 20, 16 * scale);
            GUI.Box(barRect, "");
            Rect fillRect = new Rect(barRect.x + 2, barRect.y + 2, (barRect.width - 4) * _currentProgress, barRect.height - 4);
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.95f, 0.25f, 0.65f), 0, 0);

            GUILayout.EndVertical();
            GUILayout.EndArea();

            // 2. 通关胜利弹窗
            if (lm.isLevelFinished)
            {
                DrawVictoryModal(lm, scale);
            }
        }

        private void DrawVictoryModal(VoxelLevelManager lm, float scale)
        {
            float w = Mathf.Min(Screen.width * 0.85f, 380f * scale);
            float h = 220f * scale;
            Rect modalRect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            GUI.Box(modalRect, "");
            GUILayout.BeginArea(new Rect(modalRect.x + 15 * scale, modalRect.y + 15 * scale, w - 30 * scale, h - 30 * scale));
            GUILayout.BeginVertical();

            GUILayout.Label("🎉 <b>关卡大获全胜！</b>", new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(20 * scale), alignment = TextAnchor.MiddleCenter, richText = true });
            GUILayout.Space(8 * scale);
            GUILayout.Label("目标体素已被彻底粉碎瓦解！", new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13 * scale), alignment = TextAnchor.MiddleCenter });
            GUILayout.Label($"🪙 获得通关金币: <b>+500</b>", new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(16 * scale), alignment = TextAnchor.MiddleCenter, richText = true });

            GUILayout.Space(16 * scale);
            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.45f);
            if (GUILayout.Button("➔ 进入下一关 (Next Stage)", GUILayout.Height(40 * scale)))
            {
                lm.NextLevel();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}
