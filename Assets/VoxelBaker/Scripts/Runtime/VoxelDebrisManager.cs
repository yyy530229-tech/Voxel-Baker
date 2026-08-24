using System.Collections.Generic;
using UnityEngine;

namespace VoxelBaker.Runtime
{
    /// <summary>
    /// 顶级商业化消除果冻质感与爆裂特效系统 (Juicy Voxel Pop & Blast FX)
    /// 包含：果冻膨胀爆破光环、十字闪星粒子、糖块体素飞溅、核心闪光与打击感微震
    /// </summary>
    public class VoxelDebrisManager : MonoBehaviour
    {
        public static VoxelDebrisManager Instance { get; private set; }

        [Header("材质配置")]
        public Material debrisBaseMaterial;
        public int maxPoolSize = 350;

        private Queue<GameObject> _debrisPool = new Queue<GameObject>();
        private List<DebrisItem> _activeDebris = new List<DebrisItem>();

        private Queue<GameObject> _bubblePool = new Queue<GameObject>();
        private List<BubbleItem> _activeBubbles = new List<BubbleItem>();

        private Queue<GameObject> _starPool = new Queue<GameObject>();
        private List<StarItem> _activeStars = new List<StarItem>();

        private Material _bubbleMaterial;
        private Material _starMaterial;
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

        private struct StarItem
        {
            public GameObject go;
            public Transform transform;
            public Renderer renderer;
            public Vector3 velocity;
            public float spawnTime;
            public float duration;
            public float startScale;
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
                _starMaterial = new Material(unlitShader);
            }

            InitPools();
        }

        private void InitPools()
        {
            // 1. 同色方块糖块碎屑池
            for (int i = 0; i < maxPoolSize; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Debris_{i}";
                cube.transform.SetParent(transform);

                Rigidbody rb = cube.AddComponent<Rigidbody>();
                rb.mass = 0.05f;
                rb.drag = 0.8f;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

                if (debrisBaseMaterial != null)
                {
                    cube.GetComponent<MeshRenderer>().sharedMaterial = debrisBaseMaterial;
                }

                cube.SetActive(false);
                _debrisPool.Enqueue(cube);
            }

            // 2. 膨胀爆破果冻光环球 (Bubble Shockwave)
            for (int i = 0; i < 50; i++)
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

            // 3. 闪耀星星与火花光斑 (Pop Stars)
            for (int i = 0; i < 60; i++)
            {
                GameObject star = GameObject.CreatePrimitive(PrimitiveType.Quad);
                star.name = $"Star_{i}";
                star.transform.SetParent(transform);

                Collider col = star.GetComponent<Collider>();
                if (col != null) Destroy(col);

                if (_starMaterial != null)
                {
                    star.GetComponent<MeshRenderer>().sharedMaterial = _starMaterial;
                }

                star.SetActive(false);
                _starPool.Enqueue(star);
            }
        }

        /// <summary>
        /// 触发极其 Q 弹爽快的全套消除爆破特效 (冲击波气泡 + 十字星芒 + 糖块飞溅)
        /// </summary>
        public void SpawnDebris(Vector3 worldPos, Vector3 worldNormal, Color32 color, float voxelSize, int count = 6)
        {
            // 1. 产生两层高速膨胀的消除气泡波纹 (大圈 + 内核小圈，还原参考图中的层次感)
            SpawnBubble(worldPos, color, voxelSize * 0.8f, voxelSize * 4.2f, 0.22f, 0.75f);
            SpawnBubble(worldPos, Color.white, voxelSize * 0.4f, voxelSize * 2.6f, 0.15f, 0.95f);

            // 2. 喷射 3~4 个发光的卡通爆裂星芒火花 (向四周散射)
            for (int s = 0; s < 4; s++)
            {
                Vector3 starDir = (Random.insideUnitSphere + worldNormal * 0.5f).normalized;
                SpawnStar(worldPos, starDir * Random.Range(6f, 12f), Color.white, voxelSize * 0.8f, 0.2f);
            }

            // 3. 产生 6 块 Q 萌高初速同色物理糖块碎屑
            float dScale = voxelSize * 0.48f;
            Vector3 initScale = new Vector3(dScale, dScale, dScale);

            for (int i = 0; i < count; i++)
            {
                GameObject obj = (_debrisPool.Count > 0) ? _debrisPool.Dequeue() : null;
                if (obj == null) break;

                Transform t = obj.transform;
                t.position = worldPos + Random.insideUnitSphere * (voxelSize * 0.25f);
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

                    // 向上前方扇形剧烈爆发喷射 (高初速 + 快速自然坠落)
                    Vector3 forceDir = (worldNormal * 1.2f + Random.insideUnitSphere * 0.8f + Vector3.up * 0.6f).normalized;
                    float impulse = Random.Range(6.0f, 11.5f);
                    rb.AddForce(forceDir * impulse, ForceMode.Impulse);
                    rb.AddTorque(Random.insideUnitSphere * 60f, ForceMode.Impulse);
                }

                obj.SetActive(true);

                _activeDebris.Add(new DebrisItem
                {
                    go = obj,
                    transform = t,
                    rb = rb,
                    spawnTime = Time.time,
                    lifetime = Random.Range(0.6f, 0.95f),
                    initialScale = initScale
                });
            }
        }

        private void SpawnBubble(Vector3 pos, Color32 col, float startScale, float targetScale, float duration, float initialAlpha)
        {
            GameObject bObj = (_bubblePool.Count > 0) ? _bubblePool.Dequeue() : null;
            if (bObj == null) return;

            Transform t = bObj.transform;
            t.position = pos;
            t.localScale = Vector3.one * startScale;

            Renderer r = bObj.GetComponent<Renderer>();
            Color c = Color.Lerp(col, Color.white, 0.5f);
            c.a = initialAlpha;

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

        private void SpawnStar(Vector3 pos, Vector3 velocity, Color32 col, float scale, float duration)
        {
            GameObject sObj = (_starPool.Count > 0) ? _starPool.Dequeue() : null;
            if (sObj == null) return;

            Transform t = sObj.transform;
            t.position = pos;
            t.localScale = Vector3.one * scale;
            t.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));

            Renderer r = sObj.GetComponent<Renderer>();

            sObj.SetActive(true);

            _activeStars.Add(new StarItem
            {
                go = sObj,
                transform = t,
                renderer = r,
                velocity = velocity,
                spawnTime = Time.time,
                duration = duration,
                startScale = scale,
                color = col
            });
        }

        private void Update()
        {
            float now = Time.time;
            float dt = Time.deltaTime;

            // 1. 更新糖块碎屑 (缩小弹跳消失)
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

            // 2. 更新果冻光环气泡 (极速膨胀并半透明破裂淡出)
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
                    // 弹性快速放大曲线 (EaseOutBack 风格)
                    float easeOut = 1.0f - Mathf.Pow(1.0f - progress, 3f);
                    float curScale = Mathf.Lerp(b.startScale, b.targetScale, easeOut);
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

            // 3. 更新爆裂火花闪星
            for (int i = _activeStars.Count - 1; i >= 0; i--)
            {
                StarItem s = _activeStars[i];
                float elapsed = now - s.spawnTime;
                float progress = Mathf.Clamp01(elapsed / s.duration);

                if (progress >= 1.0f)
                {
                    s.go.SetActive(false);
                    _starPool.Enqueue(s.go);
                    _activeStars.RemoveAt(i);
                }
                else
                {
                    s.transform.position += s.velocity * dt;
                    s.velocity *= 0.88f; // 快速阻尼减速

                    float curScale = Mathf.Lerp(s.startScale, 0.01f, progress);
                    s.transform.localScale = Vector3.one * curScale;
                    s.transform.Rotate(Vector3.forward, 360f * dt);
                }
            }
        }
    }
}
