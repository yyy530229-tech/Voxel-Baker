using System;
using System.Diagnostics;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelBaker.Baker;

namespace VoxelBaker.Editor
{
    /// <summary>
    /// 参数驱动的实时体素化预览面板。
    ///
    /// 三条设计约束，缺一不可：
    ///
    /// 1. 不卡编辑器 —— 体素化整体搬到 ThreadPool。之所以能这么做，是因为
    ///    MeshSnapshot 已经把 Mesh / Material / Texture 冻结成普通数组，
    ///    VoxelPreviewBuilder 全程不碰 UnityEngine 对象。
    ///
    /// 2. 拖拽要跟手 —— 两级质量：拖动中每 160ms 出一版草稿（超采样 1、网格 56³、
    ///    跳过块数标定探针），松手 400ms 后出一版成品（超采样 3、网格 112³、跑标定）。
    ///    既有实时反馈，又不会在拖动时把 CPU 钉死。
    ///
    /// 3. 预览即所得 —— 与正式烘焙共用同一套求解器，并用 VoxelPreviewLit 复刻
    ///    VoxelLit 的光照。不然又会出现"预览挺好看、烘焙出来变了个样"。
    /// </summary>
    public sealed class VoxelPreviewPanel : IDisposable
    {
        //
        // 调度参数
        //
        private const double DraftThrottleMs = 160.0;  // 草稿最小间隔
        private const double FinalDelayMs = 400.0;     // 距最后一次改动多久算"松手了"
        private const int DraftMaxGrid = 56;           // 草稿网格长轴上限
        private const int FinalMaxGrid = 112;          // 成品网格长轴上限

        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        // ---- 渲染资源 ----
        private PreviewRenderUtility _preview;
        private Mesh _mesh;
        private Material _material;
        private GUIStyle _hintStyle;

        // ---- 数据源 ----
        private Mesh _sourceMesh;
        private Material[] _sourceMaterials;
        private MeshSnapshot _snapshot;
        private int _snapshotMeshId;

        // ---- 参数模板（每帧由窗口同步进来）----
        private VoxelPreviewRequest _template;
        private bool _dirty;
        private double _lastDirtyTime;
        private double _lastDispatchTime;

        // ---- 后台任务 ----
        private CancellationTokenSource _cts;
        private volatile bool _isBuilding;
        private VoxelPreviewResult _pending;
        private readonly object _pendingLock = new object();
        private VoxelPreviewResult _displayed;

        // ---- 相机 ----
        private Vector2 _orbit = new Vector2(35f, 18f);   // x = yaw, y = pitch
        private float _distanceScale = 1.35f;
        private bool _dragging;
        private Vector2 _lastMouse;

        /// <summary>结果就绪时请求宿主窗口重绘。</summary>
        public Action RepaintRequested;

        public bool IsBuilding => _isBuilding;

        public bool HasResult => _displayed != null && _displayed.IsValid;

        public VoxelPreviewResult Result => _displayed;

        public bool HasSource => _sourceMesh != null;

        /// <summary>
        /// 每帧同步来源与参数。内部自己做差异比较，只有真的变了才会触发重建。
        /// </summary>
        public void Sync(
            Mesh sourceMesh,
            Material[] sourceMaterials,
            VoxelPreviewRequest template)
        {
            bool sourceChanged = !ReferenceEquals(sourceMesh, _sourceMesh);
            if (sourceChanged)
            {
                _sourceMesh = sourceMesh;
                _snapshot = null;
                _snapshotMeshId = 0;
            }

            bool materialsChanged = !ReferenceEquals(sourceMaterials, _sourceMaterials);
            if (materialsChanged)
            {
                _sourceMaterials = sourceMaterials;
                // 材质换了要重取贴图像素，但 MeshSnapshot 有贴图缓存，成本可控
                _snapshot = null;
                _snapshotMeshId = 0;
            }

            bool paramsChanged = _template == null || !RequestsEqual(_template, template);

            if (paramsChanged)
            {
                _template = template.Clone();
            }

            if (sourceChanged || materialsChanged || paramsChanged)
                MarkDirty();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _lastDirtyTime = Clock.Elapsed.TotalMilliseconds;
        }

        /// <summary>强制重建（例如切换 Tab 回到预览页时）。</summary>
        public void ForceRebuild()
        {
            _snapshot = null;
            _snapshotMeshId = 0;
            MarkDirty();
        }

        /// <summary>
        /// 每帧驱动：回收后台结果、按需派发新任务。必须在主线程调用。
        /// </summary>
        public void Update()
        {
            double now = Clock.Elapsed.TotalMilliseconds;

            DrainResult();

            if (!_dirty || _isBuilding || _sourceMesh == null || _template == null)
                return;

            double sinceChange = now - _lastDirtyTime;
            bool wantFinal = sinceChange >= FinalDelayMs;

            if (wantFinal || (now - _lastDispatchTime) >= DraftThrottleMs)
            {
                Dispatch(wantFinal);
                _lastDispatchTime = now;
                if (wantFinal) _dirty = false;
            }
        }

        private void DrainResult()
        {
            VoxelPreviewResult pending;
            lock (_pendingLock)
            {
                pending = _pending;
                _pending = null;
            }

            if (pending == null) return;

            _isBuilding = false;

            // 被后续请求顶掉的结果直接丢弃，不去覆盖已经显示的内容
            if (!pending.WasCancelled)
            {
                ApplyResult(pending);
                _displayed = pending;
            }

            RepaintRequested?.Invoke();
        }

        private void Dispatch(bool finalQuality)
        {
            EnsurePreview();
            EnsureSnapshot();
            EnsureMaterial();

            if (_snapshot == null || _material == null)
            {
                _isBuilding = false;
                return;
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            VoxelPreviewRequest request = _template.Clone();
            request.Snapshot = _snapshot;
            request.CancelToken = _cts.Token;

            if (finalQuality)
            {
                // 成品档：与正式烘焙完全一致的超采样与块数标定
                request.SupersampleRate = _template.SupersampleRate;
                request.MaxGridDimension = FinalMaxGrid;
                request.AccurateBudget = true;
            }
            else
            {
                // 草稿档：单点采样 + 小网格 + 跳过标定，换取拖动时的跟手
                request.SupersampleRate = 1;
                request.MaxGridDimension = DraftMaxGrid;
                request.AccurateBudget = false;
            }

            _isBuilding = true;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                VoxelPreviewResult result = VoxelPreviewBuilder.Build(request);
                lock (_pendingLock)
                {
                    _pending = result;
                }
            });
        }

        private void ApplyResult(VoxelPreviewResult result)
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                _mesh.MarkDynamic();
            }

            if (!result.IsValid)
            {
                _mesh.Clear();
                return;
            }

            _mesh.Clear();
            _mesh.indexFormat = result.Vertices.Length > 65000
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;

            _mesh.SetVertices(result.Vertices);
            _mesh.SetNormals(result.Normals);
            _mesh.SetColors(result.Colors);
            // UV1 承载"顶点在自己那颗积木内部的局部坐标"，给边缘描深与假圆角用
            _mesh.SetUVs(1, result.CubeLocal);
            _mesh.SetTriangles(result.Triangles, 0);
            _mesh.RecalculateBounds();
        }

        /// <summary>在给定矩形内渲染预览，并处理轨道拖拽与缩放。</summary>
        public void Draw(Rect rect)
        {
            if (rect.width < 16f || rect.height < 16f) return;

            HandleInput(rect);

            if (!HasResult || _mesh == null || _mesh.vertexCount == 0)
            {
                DrawPlaceholder(rect);
                return;
            }

            EnsurePreview();
            EnsureMaterial();
            if (_preview == null || _material == null) return;

            RenderScene(rect);
            DrawOverlay(rect);
        }

        private void RenderScene(Rect rect)
        {
            Bounds b = _mesh.bounds;
            float fov = _preview.camera.fieldOfView;
            float fitDist = Mathf.Max(0.001f, b.extents.magnitude) / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad);
            float dist = fitDist * _distanceScale;

            Quaternion rot = Quaternion.Euler(_orbit.y, _orbit.x, 0f);
            Vector3 dir = rot * Vector3.forward;

            _preview.camera.transform.position = b.center - dir * dist;
            _preview.camera.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            _preview.camera.nearClipPlane = Mathf.Max(0.01f, dist - b.size.magnitude);
            _preview.camera.farClipPlane = dist + b.size.magnitude * 2f + 10f;

            _preview.BeginPreview(rect, GUIStyle.none);
            _preview.DrawMesh(_mesh, Matrix4x4.identity, _material, 0);
            _preview.camera.Render();
            Texture tex = _preview.EndPreview();

            if (tex != null)
                GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);
        }

        private void DrawPlaceholder(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.90f, 0.91f, 0.93f));

            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    wordWrap = true,
                    fontSize = 12
                };
                _hintStyle.normal.textColor = new Color(0.35f, 0.37f, 0.42f);
            }

            string msg;
            if (!HasSource)
            {
                msg = "请先在步骤 ① 指定来源模型\n（把带 MeshFilter 的物体拖进去）";
            }
            else if (_isBuilding)
            {
                msg = "正在后台体素化…\n编辑器保持可交互";
            }
            else if (_displayed != null && !string.IsNullOrEmpty(_displayed.ErrorMessage))
            {
                msg = "预览构建失败：\n" + _displayed.ErrorMessage;
            }
            else
            {
                msg = "正在准备预览…";
            }

            GUI.Label(rect, msg, _hintStyle);
        }

        private void DrawOverlay(Rect rect)
        {
            // 计算中等价于"后台还在跑"，右下角给一点提示，避免用户以为卡住了
            if (!_isBuilding) return;

            Rect badge = new Rect(rect.xMax - 96f, rect.yMax - 24f, 88f, 18f);
            EditorGUI.DrawRect(badge, new Color(0.1f, 0.1f, 0.12f, 0.65f));

            GUIStyle s = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            s.normal.textColor = new Color(0.85f, 0.88f, 0.95f);
            GUI.Label(badge, "计算中…", s);
        }

        private void HandleInput(Rect rect)
        {
            Event e = Event.current;
            if (e == null) return;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && rect.Contains(e.mousePosition))
                    {
                        _dragging = true;
                        _lastMouse = e.mousePosition;
                        GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (!_dragging) break;
                    Vector2 delta = e.mousePosition - _lastMouse;
                    _lastMouse = e.mousePosition;
                    _orbit.x -= delta.x * 0.5f;
                    _orbit.y = Mathf.Clamp(_orbit.y + delta.y * 0.5f, -89f, 89f);
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (!_dragging) break;
                    _dragging = false;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;

                case EventType.ScrollWheel:
                    if (rect.Contains(e.mousePosition))
                    {
                        _distanceScale = Mathf.Clamp(_distanceScale + e.delta.y * 0.04f, 0.45f, 4.5f);
                        e.Use();
                    }
                    break;
            }
        }

        private void EnsurePreview()
        {
            if (_preview != null) return;

            _preview = new PreviewRenderUtility();
            _preview.camera.fieldOfView = 30f;
            _preview.camera.clearFlags = CameraClearFlags.SolidColor;
            _preview.camera.backgroundColor = new Color(0.86f, 0.87f, 0.89f, 1f);
            _preview.camera.allowMSAA = true;

            if (_preview.lights != null && _preview.lights.Length >= 2)
            {
                // 主光：斜上方，与正式场景的主光方向对齐，预览才不会骗人
                _preview.lights[0].type = LightType.Directional;
                _preview.lights[0].intensity = 1.15f;
                _preview.lights[0].color = Color.white;
                _preview.lights[0].transform.rotation = Quaternion.Euler(48f, 155f, 0f);

                // 补光：从另一侧压一点，防止背光面纯黑
                _preview.lights[1].type = LightType.Directional;
                _preview.lights[1].intensity = 0.32f;
                _preview.lights[1].color = new Color(0.85f, 0.90f, 1f);
                _preview.lights[1].transform.rotation = Quaternion.Euler(-25f, -70f, 0f);
            }
        }

        private void EnsureMaterial()
        {
            if (_material != null) return;

            Shader shader = Shader.Find("VoxelBaker/VoxelPreviewLit");
            if (shader == null)
            {
                UnityEngine.Debug.LogError("[VoxelPreviewPanel] 找不到 Shader 'VoxelBaker/VoxelPreviewLit'，预览无法渲染。");
                return;
            }

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private void EnsureSnapshot()
        {
            if (_sourceMesh == null)
            {
                _snapshot = null;
                return;
            }

            int id = _sourceMesh.GetInstanceID();
            if (_snapshot != null && _snapshotMeshId == id) return;

            // Capture 必须在主线程：内部读 Mesh 属性、Material 颜色、RenderTexture
            _snapshot = MeshSnapshot.Capture(_sourceMesh, _sourceMaterials);
            _snapshotMeshId = id;
        }

        private static bool RequestsEqual(VoxelPreviewRequest a, VoxelPreviewRequest b)
        {
            return Mathf.Approximately(a.TargetModelHeight, b.TargetModelHeight)
                && a.TargetVoxelBudget == b.TargetVoxelBudget
                && Mathf.Approximately(a.ManualVoxelSize, b.ManualVoxelSize)
                && a.FillStrategy == b.FillStrategy
                && a.ShellThickness == b.ShellThickness
                && a.AntiAliasing == b.AntiAliasing
                && a.SmoothingIterations == b.SmoothingIterations
                && a.SupersampleRate == b.SupersampleRate
                && a.PaletteColorCount == b.PaletteColorCount
                && Mathf.Approximately(a.PaletteTolerance, b.PaletteTolerance);
        }

        public void Dispose()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            if (_preview != null)
            {
                _preview.Cleanup();
                _preview = null;
            }

            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
                _mesh = null;
            }

            if (_material != null)
            {
                UnityEngine.Object.DestroyImmediate(_material);
                _material = null;
            }
        }
    }
}
