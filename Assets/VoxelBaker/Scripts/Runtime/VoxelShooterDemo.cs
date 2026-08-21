using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Data;

namespace VoxelBaker.Runtime
{
    public class VoxelShooterDemo : MonoBehaviour
    {
        [Header("Target Model")]
        public VoxelModelInstance targetModel;

        [Header("Shooter Cannons (Matching Screenshot)")]
        public int shooterCount = 5;
        public float shooterSpacing = 1.2f;
        public float fireRate = 12f; // 每秒射击次数
        public float bulletSpeed = 22f;
        public Color cannonColor = new Color(0.95f, 0.2f, 0.6f);

        [Header("Projectile Appearance")]
        public Material projectileMaterial;

        private List<CannonUnit> _cannons = new List<CannonUnit>();
        private List<Bullet> _activeBullets = new List<Bullet>();
        private Queue<GameObject> _bulletPool = new Queue<GameObject>();

        private struct CannonUnit
        {
            public GameObject go;
            public Transform transform;
            public int power;
            public float nextFireTime;
        }

        private struct Bullet
        {
            public GameObject go;
            public Transform transform;
            public Vector3 velocity;
            public float spawnTime;
            public int damage;
        }

        private void Start()
        {
            CreateCannons();
        }

        private void CreateCannons()
        {
            int[] powers = new int[] { 33, 45, 55, 66, 77 };
            float startX = -((shooterCount - 1) * shooterSpacing) * 0.5f;

            for (int i = 0; i < shooterCount; i++)
            {
                GameObject cannon = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cannon.name = $"Cannon_{i}";
                cannon.transform.SetParent(transform);
                cannon.transform.position = new Vector3(startX + i * shooterSpacing, -4.5f, 0f);
                cannon.transform.localScale = new Vector3(0.9f, 1.1f, 0.9f);

                Renderer r = cannon.GetComponent<Renderer>();
                if (r != null)
                {
                    Material m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    m.color = cannonColor;
                    r.sharedMaterial = m;
                }

                _cannons.Add(new CannonUnit
                {
                    go = cannon,
                    transform = cannon.transform,
                    power = (i < powers.Length) ? powers[i] : 50,
                    nextFireTime = Time.time + i * 0.08f
                });
            }

            // 初始化子弹对象池
            for (int i = 0; i < 80; i++)
            {
                GameObject bullet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bullet.name = $"Bullet_{i}";
                bullet.transform.SetParent(transform);
                bullet.transform.localScale = Vector3.one * 0.28f;
                Collider c = bullet.GetComponent<Collider>();
                if (c != null) c.enabled = false;

                if (projectileMaterial == null)
                {
                    projectileMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
                    projectileMaterial.color = Color.white;
                }
                bullet.GetComponent<Renderer>().sharedMaterial = projectileMaterial;
                bullet.SetActive(false);
                _bulletPool.Enqueue(bullet);
            }
        }

        private void Update()
        {
            if (targetModel == null) return;

            // 炮台连射逻辑
            float now = Time.time;
            for (int i = 0; i < _cannons.Count; i++)
            {
                CannonUnit cannon = _cannons[i];
                if (now >= cannon.nextFireTime)
                {
                    FireBullet(cannon);
                    cannon.nextFireTime = now + (1.0f / fireRate) + Random.Range(-0.02f, 0.02f);
                    _cannons[i] = cannon;
                }
            }

            // 更新活跃子弹飞行与射线碰撞
            UpdateBullets();
        }

        private void FireBullet(CannonUnit cannon)
        {
            GameObject bObj = (_bulletPool.Count > 0) ? _bulletPool.Dequeue() : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bObj.SetActive(true);

            Vector3 spawnPos = cannon.transform.position + Vector3.up * 0.8f + Vector3.forward * Random.Range(-0.1f, 0.1f);
            bObj.transform.position = spawnPos;

            // 射向目标模型中心稍带发散角
            Vector3 targetCenter = targetModel.transform.position;
            Vector3 aimDir = (targetCenter + Random.insideUnitSphere * 0.8f - spawnPos).normalized;

            _activeBullets.Add(new Bullet
            {
                go = bObj,
                transform = bObj.transform,
                velocity = aimDir * bulletSpeed,
                spawnTime = Time.time,
                damage = 1
            });
        }

        private void UpdateBullets()
        {
            float dt = Time.deltaTime;
            for (int i = _activeBullets.Count - 1; i >= 0; i--)
            {
                Bullet bullet = _activeBullets[i];
                Vector3 currentPos = bullet.transform.position;
                Vector3 stepMove = bullet.velocity * dt;
                Vector3 nextPos = currentPos + stepMove;

                // 进行连续射线检测
                Ray ray = new Ray(currentPos, bullet.velocity.normalized);
                float stepDist = stepMove.magnitude;

                if (targetModel.Raycast(ray, out VoxelRaycastHit hit) && hit.distance <= stepDist + 0.15f)
                {
                    // 击中体素！
                    targetModel.ApplyDamage(hit.gridPos, bullet.damage, hit.worldHitPoint, hit.hitNormal);

                    // 回收子弹
                    bullet.go.SetActive(false);
                    _bulletPool.Enqueue(bullet.go);
                    _activeBullets.RemoveAt(i);
                    continue;
                }

                // 正常移动或超时销毁
                bullet.transform.position = nextPos;
                if (Time.time - bullet.spawnTime > 3.0f || nextPos.y > 20f)
                {
                    bullet.go.SetActive(false);
                    _bulletPool.Enqueue(bullet.go);
                    _activeBullets.RemoveAt(i);
                }
            }
        }
    }
}
