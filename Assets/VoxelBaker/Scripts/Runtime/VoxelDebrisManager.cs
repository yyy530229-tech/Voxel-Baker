using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Runtime
{
    public class VoxelDebrisManager : MonoBehaviour
    {
        public static VoxelDebrisManager Instance { get; private set; }

        [Header("Debris Settings")]
        public Material debrisBaseMaterial;
        public int maxDebrisPoolSize = 250;
        public float debrisLifetime = 1.8f;
        public float minImpulse = 2.5f;
        public float maxImpulse = 6.0f;

        private Queue<GameObject> _debrisPool = new Queue<GameObject>();
        private List<DebrisItem> _activeDebris = new List<DebrisItem>();
        private MaterialPropertyBlock _propBlock;

        private struct DebrisItem
        {
            public GameObject go;
            public Transform transform;
            public float spawnTime;
            public Vector3 initialScale;
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) { Destroy(gameObject); return; }

            _propBlock = new MaterialPropertyBlock();

            if (debrisBaseMaterial == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (s != null) debrisBaseMaterial = new Material(s);
            }

            InitPool();
        }

        private void InitPool()
        {
            for (int i = 0; i < maxDebrisPoolSize; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Debris_{i}";
                cube.transform.SetParent(transform);
                
                Rigidbody rb = cube.AddComponent<Rigidbody>();
                rb.mass = 0.1f;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

                if (debrisBaseMaterial != null)
                {
                    cube.GetComponent<MeshRenderer>().sharedMaterial = debrisBaseMaterial;
                }

                cube.SetActive(false);
                _debrisPool.Enqueue(cube);
            }
        }

        public void SpawnDebris(Vector3 worldPos, Vector3 worldNormal, Color32 color, float voxelSize, int count = 3)
        {
            for (int i = 0; i < count; i++)
            {
                GameObject obj = null;
                if (_debrisPool.Count > 0)
                {
                    obj = _debrisPool.Dequeue();
                }
                else if (_activeDebris.Count > 0)
                {
                    // 复用最老的碎片
                    obj = _activeDebris[0].go;
                    _activeDebris.RemoveAt(0);
                }

                if (obj == null) return;

                obj.SetActive(true);
                Vector3 jitter = Random.insideUnitSphere * (voxelSize * 0.4f);
                obj.transform.position = worldPos + jitter;
                obj.transform.rotation = Random.rotation;

                float scaleRatio = Random.Range(0.6f, 0.95f) * voxelSize;
                obj.transform.localScale = Vector3.one * scaleRatio;

                // 设置碎片颜色
                _propBlock.SetColor("_BaseColor", color);
                _propBlock.SetColor("_Color", color);
                obj.GetComponent<MeshRenderer>().SetPropertyBlock(_propBlock);

                Rigidbody rb = obj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;

                    Vector3 impulseDir = (worldNormal + Random.insideUnitSphere * 0.7f).normalized;
                    float force = Random.Range(minImpulse, maxImpulse);
                    rb.AddForce(impulseDir * force + Vector3.up * 1.5f, ForceMode.Impulse);
                    rb.AddTorque(Random.insideUnitSphere * 15f, ForceMode.Impulse);
                }

                _activeDebris.Add(new DebrisItem
                {
                    go = obj,
                    transform = obj.transform,
                    spawnTime = Time.time,
                    initialScale = Vector3.one * scaleRatio
                });
            }
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = _activeDebris.Count - 1; i >= 0; i--)
            {
                DebrisItem item = _activeDebris[i];
                float elapsed = now - item.spawnTime;
                if (elapsed >= debrisLifetime)
                {
                    item.go.SetActive(false);
                    _debrisPool.Enqueue(item.go);
                    _activeDebris.RemoveAt(i);
                }
                else if (elapsed > debrisLifetime * 0.7f)
                {
                    // 渐进缩小消失
                    float t = (debrisLifetime - elapsed) / (debrisLifetime * 0.3f);
                    item.transform.localScale = item.initialScale * Mathf.Clamp01(t);
                }
            }
        }
    }
}
