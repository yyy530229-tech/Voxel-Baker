using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Runtime
{
    /// <summary>
    /// 高仿真物理体素爆裂喷溅系统 (完全移除多余气泡，专注于符合动量力学的海量方块爆破飞溅)
    /// </summary>
    public class VoxelDebrisManager : MonoBehaviour
    {
        public static VoxelDebrisManager Instance { get; private set; }

        [Header("物理与材质配置")]
        public Material debrisBaseMaterial;
        public int maxPoolSize = 800; // 支持炮台全开时数百块体素在空中飞舞

        private Queue<GameObject> _debrisPool = new Queue<GameObject>();
        private List<DebrisItem> _activeDebris = new List<DebrisItem>();
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

            InitPool();
        }

        private void InitPool()
        {
            for (int i = 0; i < maxPoolSize; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"PhysicalDebris_{i}";
                cube.transform.SetParent(transform);

                Collider col = cube.GetComponent<Collider>();
                if (col != null) col.enabled = false; // 移除互相阻挡以获得纯粹弹道抛物线

                Rigidbody rb = cube.AddComponent<Rigidbody>();
                rb.mass = 0.02f;
                rb.drag = 0.08f;          // 低空气阻力，形成极其舒展优美的抛物线
                rb.angularDrag = 0.1f;
                rb.useGravity = true;

                if (debrisBaseMaterial != null)
                {
                    cube.GetComponent<MeshRenderer>().sharedMaterial = debrisBaseMaterial;
                }

                cube.SetActive(false);
                _debrisPool.Enqueue(cube);
            }
        }

        /// <summary>
        /// 触发极其逼真的动量爆炸力学碎屑飞溅 (20~28 块向外锥形散射，完全无气泡)
        /// </summary>
        public void SpawnDebris(Vector3 worldPos, Vector3 worldNormal, Color32 color, float voxelSize, int count = 24)
        {
            // 真实物理碎屑飞散：根据受击法线 + 子弹冲击力形成扇面锥形爆发
            for (int i = 0; i < count; i++)
            {
                GameObject obj = (_debrisPool.Count > 0) ? _debrisPool.Dequeue() : null;
                if (obj == null) break;

                Transform t = obj.transform;
                t.position = worldPos + Random.insideUnitSphere * (voxelSize * 0.4f);
                t.rotation = Random.rotation;

                // 产生多尺度碎块 (大中小碎片层次分明)
                float scaleMultiplier = Random.Range(0.28f, 0.62f);
                float dScale = voxelSize * scaleMultiplier;
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

                    // 1. 严格符合力学的受击散射方向：
                    // 法线冲量 + 强劲的向外径向发散 + 适量向上反弹
                    Vector3 radialSpread = Random.insideUnitSphere;
                    // 确保主要朝外侧和前上方扇形炸开，绝非机械竖直下落
                    Vector3 burstDir = (worldNormal * 1.8f + radialSpread * 1.5f + Vector3.up * 0.9f + Vector3.back * 0.6f).normalized;

                    // 2. 强劲的初速爆炸动能 (8~16 m/s)
                    float impulseSpeed = Random.Range(8.5f, 16.0f);
                    rb.velocity = burstDir * impulseSpeed;

                    // 3. 高速旋转翻滚力矩
                    rb.angularVelocity = Random.insideUnitSphere * Random.Range(30f, 75f);
                }

                obj.SetActive(true);

                _activeDebris.Add(new DebrisItem
                {
                    go = obj,
                    transform = t,
                    rb = rb,
                    spawnTime = Time.time,
                    lifetime = Random.Range(0.9f, 1.4f),
                    initialScale = initScale
                });
            }
        }

        private void Update()
        {
            float now = Time.time;

            // 更新海量物理碎屑 (遵循重力与抛物线，并在后半段平滑自然缩小消隐)
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
                else if (age > item.lifetime * 0.55f)
                {
                    // 在飞行后半段优雅淡出缩小，不产生突兀闪烁
                    float fade = 1.0f - (age - item.lifetime * 0.55f) / (item.lifetime * 0.45f);
                    item.transform.localScale = item.initialScale * Mathf.Max(0.01f, fade);
                }
            }
        }
    }
}
