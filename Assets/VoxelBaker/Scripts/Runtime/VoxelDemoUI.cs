using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Runtime
{
    /// <summary>
    /// 运行时演示 HUD 控制面板（提供模型切换、射速调节、重置、体素实时破坏率统计）
    /// </summary>
    public class VoxelDemoUI : MonoBehaviour
    {
        public VoxelModelInstance targetModelInstance;
        public VoxelShooterDemo shooterDemo;

        public VoxelAsset duckAsset;
        public VoxelAsset pinkHeadAsset;
        public VoxelAsset houseAsset;

        private float _fps = 60f;
        private float _fpsTimer = 0f;

        private void Update()
        {
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer > 0.3f)
            {
                _fps = 1.0f / Mathf.Max(1e-4f, Time.unscaledDeltaTime);
                _fpsTimer = 0f;
            }

            // 支持鼠标左键点击直接用 DDA 射线手动破坏体素
            if (Input.GetMouseButton(0) && targetModelInstance != null && Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (targetModelInstance.Raycast(ray, out VoxelRaycastHit hit, 100f))
                {
                    targetModelInstance.ApplyDamage(hit.gridPos, 1, hit.worldHitPoint, hit.hitNormal);
                }
            }
        }

        private void OnGUI()
        {
            GUI.skin.box.fontSize = 12;
            GUI.skin.button.fontSize = 12;

            // 顶部信息栏
            GUILayout.BeginArea(new Rect(16, 16, 320, 360));
            GUILayout.BeginVertical("box");

            GUILayout.Label("🎮 <b>Voxel Baker 运行时破坏演示</b>", new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true });
            GUILayout.Label($"⚡ FPS: {Mathf.RoundToInt(_fps)} | 渲染后端: GPU Indirect Draw");

            if (targetModelInstance != null && targetModelInstance.Asset != null)
            {
                int active = targetModelInstance.ActiveVoxelCount;
                int destroyed = targetModelInstance.DestroyedVoxelCount;
                int total = active + destroyed;
                float ratio = total > 0 ? (float)destroyed / total * 100f : 0f;

                GUILayout.Space(6);
                GUILayout.Label($"📦 当前模型: <b>{targetModelInstance.Asset.name}</b>", new GUIStyle(GUI.skin.label) { richText = true });
                GUILayout.Label($"🟢 存活体素: {active:N0} | 💥 已破坏: {destroyed:N0}");
                GUILayout.Label($"🔥 破坏进度: {ratio:F1}%");

                // 进度条
                Rect r = GUILayoutUtility.GetRect(300, 16);
                GUI.Box(r, "");
                Rect fillR = new Rect(r.x, r.y, r.width * (ratio / 100f), r.height);
                GUI.DrawTexture(fillR, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.95f, 0.25f, 0.55f), 0, 0);
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>切换演示模型:</b>", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🦆 小黄鸭") && duckAsset != null)
            {
                SwitchAsset(duckAsset);
            }
            if (GUILayout.Button("🌸 粉色多层头颅") && pinkHeadAsset != null)
            {
                SwitchAsset(pinkHeadAsset);
            }
            if (GUILayout.Button("🏠 房子") && houseAsset != null)
            {
                SwitchAsset(houseAsset);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("<b>炮台射速控制:</b>", new GUIStyle(GUI.skin.label) { richText = true });
            if (shooterDemo != null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("正常 (12/s)")) shooterDemo.fireRate = 12f;
                if (GUILayout.Button("极速 (24/s)")) shooterDemo.fireRate = 24f;
                if (GUILayout.Button("狂暴 (48/s)")) shooterDemo.fireRate = 48f;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            if (GUILayout.Button("↺ 重置当前模型", GUILayout.Height(30)))
            {
                targetModelInstance?.InitializeModel();
            }

            GUILayout.Space(4);
            GUILayout.Label("💡 <i>提示: 点击/拖拽鼠标左键也可直接手动破坏体素</i>", new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true });

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void SwitchAsset(VoxelAsset asset)
        {
            if (targetModelInstance != null && asset != null)
            {
                targetModelInstance.voxelAsset = asset;
                targetModelInstance.InitializeModel();
            }
        }
    }
}
