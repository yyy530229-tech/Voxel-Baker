using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Runtime
{
    /// <summary>
    /// 高质感聚焦物理体素碎屑管理器 (适量 3~4 块微型方块原位炸裂，干净利落不乱飞)
    /// </summary>
    public class VoxelDebrisManager : MonoBehaviour
    {
        public static VoxelDebrisManager Instance { get; private set; }

        [Header("材质配置")]
        public Material debrisBaseMaterial;
        public int maxPoolSize = 200;

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
                cube.name = $"Debris_{i}";
                cube.transform.SetParent(transform);

                Collider col = cube.GetComponent<Collider>();
                if (col != null) col.enabled = false;

                Rigidbody rb = cube.AddComponent<Rigidbody>();
                rb.mass = 0.05f;
                rb.drag = 1.2f;        // 适量空气阻力，保持紧凑聚集
                rb.angularDrag = 0.5f;
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
        /// 精确原位产生 3~4 块微型同色物理方块 (小范围聚集爆裂，0.5秒自然淡出，干净清爽)
        /// </summary>
        public void SpawnDebris(Vector3 worldPos, Vector3 worldNormal, Color32 color, float voxelSize, int count = 4)
        {
            float dScale = voxelSize * 0.38f;
            Vector3 initScale = new Vector3(dScale, dScale, dScale);

            for (int i = 0; i < count; i++)
            {
                GameObject obj = (_debrisPool.Count > 0) ? _debrisPool.Dequeue() : null;
                if (obj == null) break;

                Transform t = obj.transform;
                t.position = worldPos + Random.insideUnitSphere * (voxelSize * 0.18f);
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

                    // 紧凑小范围向外轻微弹射 (3.0 ~ 5.5 m/s 适度初速)
                    Vector3 burstDir = (worldNormal * 1.0f + Random.insideUnitSphere * 0.6f + Vector3.up * 0.4f).normalized;
                    float impulse = Random.Range(3.2f, 5.8f);
                    rb.velocity = burstDir * impulse;
                    rb.angularVelocity = Random.insideUnitSphere * 35f;
                }

                obj.SetActive(true);

                _activeDebris.Add(new DebrisItem
                {
                    go = obj,
                    transform = t,
                    rb = rb,
                    spawnTime = Time.time,
                    lifetime = Random.Range(0.45f, 0.65f), // 快速消隐
                    initialScale = initScale
                });
            }
        }

        private void Update()
        {
            float now = Time.time;

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
                else if (age > item.lifetime * 0.4f)
                {
                    float fade = 1.0f - (age - item.lifetime * 0.4f) / (item.lifetime * 0.6f);
                    item.transform.localScale = item.initialScale * Mathf.Max(0.01f, fade);
                }
            }
        }
    }
}
