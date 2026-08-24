using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Runtime
{
    /// <summary>
    /// 体素爆裂与海量碎屑喷溅系统 (匹配参考图2: 清爽单层冲击光环 + 震撼漫天方块喷射碎屑)
    /// </summary>
    public class VoxelDebrisManager : MonoBehaviour
    {
        public static VoxelDebrisManager Instance { get; private set; }

        [Header("材质配置")]
        public Material debrisBaseMaterial;
        public int maxPoolSize = 600; // 支持炮台全开时上百块体素漫天飞溅

        private Queue<GameObject> _debrisPool = new Queue<GameObject>();
        private List<DebrisItem> _activeDebris = new List<DebrisItem>();

        private Queue<GameObject> _bubblePool = new Queue<GameObject>();
        private List<BubbleItem> _activeBubbles = new List<BubbleItem>();

        private Material _bubbleMaterial;
        private MaterialPropertyBlock _propBlock;

        private struct DebrisItem
        {
            public GameObject go;
            public Transform transform;
            public Rigidbody rb;
            public float spawnTime;
            public float lifetime;
            public Vector3 initialScale;
        }

        private struct BubbleItem
        {
            public GameObject go;
            public Transform transform;
            public Renderer renderer;
            public float spawnTime;
            public float duration;
            public float startScale;
            public float targetScale;
            public Color color;
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) { Destroy(gameObject); return; }

            _propBlock = new MaterialPropertyBlock();

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (debrisBaseMaterial == null && litShader != null)
            {
                debrisBaseMaterial = new Material(litShader);
            }

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (unlitShader != null)
            {
                _bubbleMaterial = new Material(unlitShader);
            }

            InitPools();
        }

        private void InitPools()
        {
            // 1. 海量同色方块碎屑池 (600 容量)
            for (int i = 0; i < maxPoolSize; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Debris_{i}";
                cube.transform.SetParent(transform);

                Rigidbody rb = cube.AddComponent<Rigidbody>();
                rb.mass = 0.04f;
                rb.drag = 0.4f;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

                if (debrisBaseMaterial != null)
                {
                    cube.GetComponent<MeshRenderer>().sharedMaterial = debrisBaseMaterial;
                }

                cube.SetActive(false);
                _debrisPool.Enqueue(cube);
            }

            // 2. 清爽轻量单层气泡光环池
            for (int i = 0; i < 40; i++)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"Bubble_{i}";
                sphere.transform.SetParent(transform);

                Collider col = sphere.GetComponent<Collider>();
                if (col != null) Destroy(col);

                if (_bubbleMaterial != null)
                {
                    sphere.GetComponent<MeshRenderer>().sharedMaterial = _bubbleMaterial;
                }

                sphere.SetActive(false);
                _bubblePool.Enqueue(sphere);
            }
        }

        /// <summary>
        /// 触发震撼的海量体素方块喷射爆裂 (12~16 块漫天飞溅) + 清爽单层微光环
        /// </summary>
        public void SpawnDebris(Vector3 worldPos, Vector3 worldNormal, Color32 color, float voxelSize, int count = 14)
        {
            // 1. 生成单层清爽半透明微扩散光环 (轻盈不遮挡)
            SpawnSingleBubble(worldPos, color, voxelSize * 0.9f, voxelSize * 3.2f, 0.20f);

            // 2. 产生 12~16 块多尺度高初速喷射体素方块 (打造参考图2整片消除的震撼飞溅雨)
            for (int i = 0; i < count; i++)
            {
                GameObject obj = (_debrisPool.Count > 0) ? _debrisPool.Dequeue() : null;
                if (obj == null) break;

                Transform t = obj.transform;
                t.position = worldPos + Random.insideUnitSphere * (voxelSize * 0.35f);
                t.rotation = Random.rotation;

                // 随机大小尺度，增强视觉层次感
                float scaleRatio = Random.Range(0.35f, 0.65f);
                float dScale = voxelSize * scaleRatio;
                Vector3 initScale = new Vector3(dScale, dScale, dScale);
                t.localScale = initScale;

                Renderer r = obj.GetComponent<Renderer>();
                if (r != null)
                {
                    _propBlock.SetColor("_BaseColor", color);
                    _propBlock.SetColor("_Color", color);
                    r.SetPropertyBlock(_propBlock);
                }

                Rigidbody rb = obj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;

                    // 向上前方与外侧扇形剧烈大范围喷溅 (高初速 + 强烈自转)
                    Vector3 spread = Random.insideUnitSphere * 1.1f;
                    Vector3 forceDir = (worldNormal * 1.5f + spread + Vector3.up * 0.8f).normalized;
                    float impulse = Random.Range(6.5f, 13.5f);
                    rb.AddForce(forceDir * impulse, ForceMode.Impulse);
                    rb.AddTorque(Random.insideUnitSphere * 90f, ForceMode.Impulse);
                }

                obj.SetActive(true);

                _activeDebris.Add(new DebrisItem
                {
                    go = obj,
                    transform = t,
                    rb = rb,
                    spawnTime = Time.time,
                    lifetime = Random.Range(0.85f, 1.35f),
                    initialScale = initScale
                });
            }
        }

        private void SpawnSingleBubble(Vector3 pos, Color32 col, float startScale, float targetScale, float duration)
        {
            GameObject bObj = (_bubblePool.Count > 0) ? _bubblePool.Dequeue() : null;
            if (bObj == null) return;

            Transform t = bObj.transform;
            t.position = pos;
            t.localScale = Vector3.one * startScale;

            Renderer r = bObj.GetComponent<Renderer>();
            Color c = Color.Lerp(col, Color.white, 0.6f);
            c.a = 0.45f; // 清爽轻透

            bObj.SetActive(true);

            _activeBubbles.Add(new BubbleItem
            {
                go = bObj,
                transform = t,
                renderer = r,
                spawnTime = Time.time,
                duration = duration,
                startScale = startScale,
                targetScale = targetScale,
                color = c
            });
        }

        private void Update()
        {
            float now = Time.time;

            // 1. 更新海量体素方块飞溅与自然消隐
            for (int i = _activeDebris.Count - 1; i >= 0; i--)
            {
                DebrisItem item = _activeDebris[i];
                float age = now - item.spawnTime;

                if (age > item.lifetime)
                {
                    item.go.SetActive(false);
                    _debrisPool.Enqueue(item.go);
                    _activeDebris.RemoveAt(i);
                }
                else if (age > item.lifetime * 0.5f)
                {
                    float fade = 1.0f - (age - item.lifetime * 0.5f) / (item.lifetime * 0.5f);
                    item.transform.localScale = item.initialScale * Mathf.Max(0.01f, fade);
                }
            }

            // 2. 更新单层轻透微光环
            for (int i = _activeBubbles.Count - 1; i >= 0; i--)
            {
                BubbleItem b = _activeBubbles[i];
                float elapsed = now - b.spawnTime;
                float progress = Mathf.Clamp01(elapsed / b.duration);

                if (progress >= 1.0f)
                {
                    b.go.SetActive(false);
                    _bubblePool.Enqueue(b.go);
                    _activeBubbles.RemoveAt(i);
                }
                else
                {
                    float curScale = Mathf.Lerp(b.startScale, b.targetScale, Mathf.Sqrt(progress));
                    b.transform.localScale = Vector3.one * curScale;

                    if (b.renderer != null)
                    {
                        Color c = b.color;
                        c.a = (1.0f - progress) * b.color.a;
                        _propBlock.SetColor("_BaseColor", c);
                        _propBlock.SetColor("_Color", c);
                        b.renderer.SetPropertyBlock(_propBlock);
                    }
                }
            }
        }
    }
}
