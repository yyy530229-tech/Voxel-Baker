using System.Collections.Generic;
using UnityEngine;
using VoxelBaker.Runtime;
using VoxelGameFramework.Projectile;

namespace VoxelGameFramework.Cannons
{
    public class VoxelCannonSquad : MonoBehaviour
    {
        [Header("炮台编队布局")]
        public int cannonCount = 5;
        public float spacing = 1.15f;
        public float baseYPosition = -4.5f;

        [Header("弹药材质")]
        public Material bulletMaterial;

        [Header("目标关联")]
        public VoxelModelInstance targetModel;

        private List<VoxelCannonUnit> _cannons = new List<VoxelCannonUnit>();
        private Queue<VoxelBullet> _bulletPool = new Queue<VoxelBullet>();
        private List<VoxelBullet> _activeBullets = new List<VoxelBullet>();

        public List<VoxelCannonUnit> Cannons => _cannons;

        public void SetupSquad(int[] powers, Color squadColor)
        {
            // 清理旧炮台
            foreach (var c in _cannons)
            {
                if (c != null) Destroy(c.gameObject);
            }
            _cannons.Clear();

            int count = (powers != null && powers.Length > 0) ? powers.Length : cannonCount;
            float startX = -((count - 1) * spacing) * 0.5f;

            for (int i = 0; i < count; i++)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Cannon_{i}";
                cube.transform.SetParent(transform);
                cube.transform.position = new Vector3(startX + i * spacing, baseYPosition, 0f);
                cube.transform.localScale = new Vector3(0.9f, 1.1f, 0.9f);

                VoxelCannonUnit unit = cube.AddComponent<VoxelCannonUnit>();
                int p = (powers != null && i < powers.Length) ? powers[i] : (30 + i * 10);
                unit.Initialize(i, p, squadColor);

                _cannons.Add(unit);
            }

            InitBulletPool(120);
        }

        private void InitBulletPool(int count)
        {
            if (bulletMaterial == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                bulletMaterial = new Material(s) { color = Color.white };
            }

            for (int i = 0; i < count; i++)
            {
                GameObject bObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bObj.name = $"PooledBullet_{i}";
                bObj.transform.SetParent(transform);
                bObj.transform.localScale = Vector3.one * 0.28f;

                Collider col = bObj.GetComponent<Collider>();
                if (col != null) col.enabled = false;

                bObj.GetComponent<Renderer>().sharedMaterial = bulletMaterial;

                VoxelBullet bullet = bObj.AddComponent<VoxelBullet>();
                bObj.SetActive(false);
                _bulletPool.Enqueue(bullet);
            }
        }

        private void Update()
        {
            if (targetModel == null) return;

            float now = Time.time;

            // 瞄准点计算：如果有鼠标点击拖拽，瞄准鼠标点；否则自动在目标模型区域平滑扫描
            Vector3 aimPos = targetModel.transform.position;
            if (Input.GetMouseButton(0) && Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                Plane plane = new Plane(Vector3.forward, targetModel.transform.position);
                if (plane.Raycast(ray, out float enter))
                {
                    aimPos = ray.GetPoint(enter);
                }
            }
            else
            {
                // 平滑扫射
                float sweepX = Mathf.Sin(now * 2.5f) * 1.5f;
                aimPos += new Vector3(sweepX, 0f, 0f);
            }

            for (int i = 0; i < _cannons.Count; i++)
            {
                _cannons[i].Tick(now, aimPos, targetModel, SpawnBullet);
            }
        }

        private void SpawnBullet(Vector3 startPos, Vector3 dir, int damage, float speed, VoxelModelInstance target)
        {
            VoxelBullet b = (_bulletPool.Count > 0) ? _bulletPool.Dequeue() : CreateNewBullet();
            b.Launch(startPos, dir, damage, speed, target, RecycleBullet);
            _activeBullets.Add(b);
        }

        private VoxelBullet CreateNewBullet()
        {
            GameObject bObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bObj.transform.SetParent(transform);
            bObj.transform.localScale = Vector3.one * 0.28f;
            Collider col = bObj.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            bObj.GetComponent<Renderer>().sharedMaterial = bulletMaterial;
            return bObj.AddComponent<VoxelBullet>();
        }

        private void RecycleBullet(VoxelBullet bullet)
        {
            _activeBullets.Remove(bullet);
            _bulletPool.Enqueue(bullet);
        }

        public void UpgradeAllCannons(int powerDelta)
        {
            foreach (var c in _cannons)
            {
                if (c != null) c.UpgradePower(powerDelta);
            }
        }
    }
}
