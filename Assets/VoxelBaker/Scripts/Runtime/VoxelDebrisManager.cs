using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Runtime
{
    /// <summary>
    /// 体素爆裂特效与碎屑管理器 (匹配参考图2: 扩散冲击波光环 + 同色方块爆破飞溅 + 受击微闪)
    /// </summary>
    public class VoxelDebrisManager : MonoBehaviour
    {
        public static VoxelDebrisManager Instance { get; private set; }

        [Header("Debris Settings")]
        public Material debrisBaseMaterial;
        public int maxDebrisPoolSize = 300;
        public float debrisLifetime = 1.2f;

        private Queue<GameObject> _debrisPool = new Queue<GameObject>();
        private List<DebrisItem> _activeDebris = new List<DebrisItem>();
        private Queue<GameObject> _ringPool = new Queue<GameObject>();
        private List<RingItem> _activeRings = new List<RingItem>();

        private Material _ringMaterial;
        private MaterialPropertyBlock _propBlock;

        private struct DebrisItem
        {
            public GameObject go;
            public Transform transform;
            public Rigidbody rb;
            public float spawnTime;
            public Vector3 initialScale;
        }

        private struct RingItem
        {
            public GameObject go;
            public Transform transform;
            public Renderer renderer;
            public float spawnTime;
            public float duration;
            public float startScale;
            public float targetScale;
            public Color baseColor;
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

            // 创建半透明扩散光环材质
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (unlitShader != null)
            {
                _ringMaterial = new Material(unlitShader);
                _ringMaterial.color = Color.white;
            }

            InitPools();
        }

        private void InitPools()
        {
            // 1. 方块碎屑池
            for (int i = 0; i < maxDebrisPoolSize; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Debris_{i}";
                cube.transform.SetParent(transform);

                Rigidbody rb = cube.AddComponent<Rigidbody>();
                rb.mass = 0.08f;
                rb.drag = 0.5f;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

                if (debrisBaseMaterial != null)
                {
                    cube.GetComponent<MeshRenderer>().sharedMaterial = debrisBaseMaterial;
                }

                cube.SetActive(false);
                _debrisPool.Enqueue(cube);
            }

            // 2. 冲击波扩散光环池 (匹配截图2中屋顶爆出的半透明圆形冲击波)
            for (int i = 0; i < 40; i++)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"ShockwaveRing_{i}";
                sphere.transform.SetParent(transform);

                Collider col = sphere.GetComponent<Collider>();
                if (col != null) Destroy(col);

                if (_ringMaterial != null)
                {
                    sphere.GetComponent<MeshRenderer>().sharedMaterial = _ringMaterial;
                }

                sphere.SetActive(false);
                _ringPool.Enqueue(sphere);
            }
        }

        public void SpawnDebris(Vector3 worldPos, Vector3 worldNormal, Color32 color, float voxelSize, int count = 5)
        {
            // 1. 生成半透明扩散冲击波光环 (匹配参考图中的大范围爆破光圈)
            SpawnShockwaveRing(worldPos, color, voxelSize * 1.5f, voxelSize * 4.5f, 0.28f);

            // 2. 产生 4~6 块高速喷溅的同色物理微型体素
            float dScale = voxelSize * 0.45f;
            Vector3 initScale = new Vector3(dScale, dScale, dScale);

            for (int i = 0; i < count; i++)
            {
                GameObject obj = (_debrisPool.Count > 0) ? _debrisPool.Dequeue() : null;
                if (obj == null) break;

                Transform t = obj.transform;
                t.position = worldPos + Random.insideUnitSphere * (voxelSize * 0.3f);
                t.rotation = Random.rotation;
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

                    // 向上前方扇形剧烈爆发喷射
                    Vector3 forceDir = (worldNormal + Random.insideUnitSphere * 0.85f + Vector3.up * 0.5f).normalized;
                    float impulse = Random.Range(4.5f, 9.0f);
                    rb.AddForce(forceDir * impulse, ForceMode.Impulse);
                    rb.AddTorque(Random.insideUnitSphere * 40f, ForceMode.Impulse);
                }

                obj.SetActive(true);

                _activeDebris.Add(new DebrisItem
                {
                    go = obj,
                    transform = t,
                    rb = rb,
                    spawnTime = Time.time,
                    initialScale = initScale
                });
            }
        }

        private void SpawnShockwaveRing(Vector3 worldPos, Color32 color, float startScale, float targetScale, float duration)
        {
            GameObject ringObj = (_ringPool.Count > 0) ? _ringPool.Dequeue() : null;
            if (ringObj == null) return;

            Transform t = ringObj.transform;
            t.position = worldPos;
            t.localScale = Vector3.one * startScale;

            Renderer r = ringObj.GetComponent<Renderer>();
            Color c = Color.Lerp(color, Color.white, 0.65f); // 亮白微带色晕
            c.a = 0.55f;

            ringObj.SetActive(true);

            _activeRings.Add(new RingItem
            {
                go = ringObj,
                transform = t,
                renderer = r,
                spawnTime = Time.time,
                duration = duration,
                startScale = startScale,
                targetScale = targetScale,
                baseColor = c
            });
        }

        private void Update()
        {
            float now = Time.time;

            // 更新物理方块消隐
            for (int i = _activeDebris.Count - 1; i >= 0; i--)
            {
                DebrisItem item = _activeDebris[i];
                float age = now - item.spawnTime;

                if (age > debrisLifetime)
                {
                    item.go.SetActive(false);
                    _debrisPool.Enqueue(item.go);
                    _activeDebris.RemoveAt(i);
                }
                else if (age > debrisLifetime * 0.6f)
                {
                    float fade = 1.0f - (age - debrisLifetime * 0.6f) / (debrisLifetime * 0.4f);
                    item.transform.localScale = item.initialScale * Mathf.Max(0.01f, fade);
                }
            }

            // 更新扩散冲击波光环动画 (匹配参考图中的半透明膨胀光圈)
            for (int i = _activeRings.Count - 1; i >= 0; i--)
            {
                RingItem ring = _activeRings[i];
                float elapsed = now - ring.spawnTime;
                float progress = Mathf.Clamp01(elapsed / ring.duration);

                if (progress >= 1.0f)
                {
                    ring.go.SetActive(false);
                    _ringPool.Enqueue(ring.go);
                    _activeRings.RemoveAt(i);
                }
                else
                {
                    // 快速膨胀
                    float curScale = Mathf.Lerp(ring.startScale, ring.targetScale, Mathf.Sqrt(progress));
                    ring.transform.localScale = Vector3.one * curScale;

                    // 渐隐淡出
                    if (ring.renderer != null)
                    {
                        Color c = ring.baseColor;
                        c.a = (1.0f - progress) * 0.55f;
                        _propBlock.SetColor("_BaseColor", c);
                        _propBlock.SetColor("_Color", c);
                        ring.renderer.SetPropertyBlock(_propBlock);
                    }
                }
            }
        }
    }
}
