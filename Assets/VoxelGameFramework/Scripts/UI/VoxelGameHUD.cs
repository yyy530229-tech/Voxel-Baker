using UnityEngine;
using VoxelGameFramework.Cannons;
using VoxelGameFramework.Core;
using VoxelGameFramework.Level;

namespace VoxelGameFramework.UI
{
    /// <summary>
    /// 独立游戏 HUD 界面控制器（提供关卡进度、金币数值、炮台升级与通关胜利弹窗）
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

            GUI.skin.box.fontSize = 13;
            GUI.skin.button.fontSize = 13;

            // 1. 顶部游戏状态栏 (关卡名、金币、破坏进度条)
            float screenW = Screen.width;
            GUILayout.BeginArea(new Rect(screenW * 0.5f - 240, 16, 480, 110));
            GUILayout.BeginVertical("box");

            GUILayout.BeginHorizontal();
            string lvlName = (lm.levelPlaylists.Count > lm.currentLevelIndex) ? lm.levelPlaylists[lm.currentLevelIndex].levelTitle : "关卡进行中";
            GUILayout.Label($"🏆 <b>{lvlName}</b>", new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true });
            GUILayout.FlexibleSpace();
            GUILayout.Label($"💰 <b>{lm.totalCoins:N0}</b>  |  ⚡ FPS: {Mathf.RoundToInt(_fps)}", new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true });
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label($"🔥 目标粉碎进度: <b>{(_currentProgress * 100f):F1}%</b>  (剩余体素: {_activeVoxels:N0} / {_totalVoxels:N0})", new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true });

            // 渐变粉色进度条
            Rect barRect = GUILayoutUtility.GetRect(460, 18);
            GUI.Box(barRect, "");
            Rect fillRect = new Rect(barRect.x + 2, barRect.y + 2, (barRect.width - 4) * _currentProgress, barRect.height - 4);
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.95f, 0.22f, 0.62f), 0, 0);

            GUILayout.EndVertical();
            GUILayout.EndArea();

            // 2. 底部功能栏 (升级炮台 / 下一关 / 重置)
            GUILayout.BeginArea(new Rect(screenW * 0.5f - 240, Screen.height - 75, 480, 60));
            GUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.25f, 0.85f, 0.45f);
            if (GUILayout.Button("⚡ 炮台全面升级 (+10威力)", GUILayout.Height(42)))
            {
                if (lm.totalCoins >= 100)
                {
                    lm.totalCoins -= 100;
                    lm.cannonSquad?.UpgradeAllCannons(10);
                }
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("↺ 重玩本关", GUILayout.Height(42), GUILayout.Width(100)))
            {
                lm.RestartCurrentLevel();
            }

            if (GUILayout.Button("⏭ 下一关", GUILayout.Height(42), GUILayout.Width(100)))
            {
                lm.NextLevel();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // 3. 通关胜利弹窗
            if (lm.isLevelFinished)
            {
                DrawVictoryModal(lm);
            }
        }

        private void DrawVictoryModal(VoxelLevelManager lm)
        {
            float w = 360;
            float h = 240;
            Rect modalRect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            GUI.Box(modalRect, "");
            GUILayout.BeginArea(new Rect(modalRect.x + 20, modalRect.y + 20, w - 40, h - 40));
            GUILayout.BeginVertical();

            GUILayout.Label("🎉 <b>关卡大获全胜！</b>", new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, richText = true });
            GUILayout.Space(8);
            GUILayout.Label("目标体素已被彻底粉碎瓦解！", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Label($"🪙 获得通关金币: <b>+500</b>", new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, richText = true });

            GUILayout.Space(18);
            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.45f);
            if (GUILayout.Button("➔ 进入下一关 (Next Stage)", GUILayout.Height(44)))
            {
                lm.NextLevel();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}
