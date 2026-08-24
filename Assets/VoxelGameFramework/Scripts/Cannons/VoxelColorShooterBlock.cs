using System;
using UnityEngine;
using VoxelBaker.Runtime;
using VoxelGameFramework.Core;

namespace VoxelGameFramework.Cannons
{
    public enum ShooterBlockState
    {
        InQueue,
        MovingToSlot,
        ActiveInSlot,
        Disappearing
    }

    /// <summary>
    /// 彩色射击方块单元 (匹配参考图2)
    /// 只能射击与自身颜色相同的体素，每次射中减少 1 点计数，归零后自动爆裂消失并释放槽位！
    /// </summary>
    public class VoxelColorShooterBlock : MonoBehaviour
    {
        [Header("方块属性")]
        public Color32 blockColor = new Color32(230, 40, 50, 255);
        public int initialCapacity = 50;
        public int remainingAmmo = 50;
        public float fireRate = 8.5f; // 每秒射击发数
        public float bulletSpeed = 28f;

        [Header("状态")]
        public ShooterBlockState state = ShooterBlockState.InQueue;
        public int currentSlotIndex = -1;

        private Transform _transform;
        private Vector3 _targetSlotPos;
        private float _moveStartTime;
        private float _nextFireTime = 0f;
        private TextMesh _numberText;
        private VoxelModelInstance _targetModel;
        private Action<VoxelColorShooterBlock> _onEmptyCallback;
        private Renderer _renderer;
        private MaterialPropertyBlock _propBlock;

        public void Initialize(Color32 color, int capacity, VoxelModelInstance targetModel, Action<VoxelColorShooterBlock> onEmpty)
        {
            _transform = transform;
            blockColor = color;
            initialCapacity = capacity;
            remainingAmmo = capacity;
            _targetModel = targetModel;
            _onEmptyCallback = onEmpty;
            state = ShooterBlockState.InQueue;

            SetupVisuals();
        }

        private void SetupVisuals()
        {
            _propBlock = new MaterialPropertyBlock();
            _renderer = GetComponent<Renderer>();

            if (_renderer != null)
            {
                Material m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                _renderer.sharedMaterial = m;
                _propBlock.SetColor("_BaseColor", blockColor);
                _propBlock.SetColor("_Color", blockColor);
                _renderer.SetPropertyBlock(_propBlock);
            }

            // 3D 数字标签 (显示在方块正前方)
            Transform textChild = transform.Find("AmmoNumberText");
            if (textChild == null)
            {
                GameObject textObj = new GameObject("AmmoNumberText");
                textObj.transform.SetParent(transform);
                textObj.transform.localPosition = new Vector3(0f, 0.05f, -0.52f);
                textObj.transform.localRotation = Quaternion.identity;
                textObj.transform.localScale = Vector3.one * 0.13f;

                _numberText = textObj.AddComponent<TextMesh>();
                _numberText.alignment = TextAlignment.Center;
                _numberText.anchor = TextAnchor.MiddleCenter;
                _numberText.fontSize = 38;
                _numberText.fontStyle = FontStyle.Bold;
                _numberText.color = Color.white;
            }
            else
            {
                _numberText = textChild.GetComponent<TextMesh>();
            }

            UpdateNumberDisplay();
        }

        public void UpdateNumberDisplay()
        {
            if (_numberText != null)
            {
                _numberText.text = remainingAmmo.ToString();
            }
        }

        public void MoveToSlot(int slotIndex, Vector3 slotWorldPos)
        {
            state = ShooterBlockState.MovingToSlot;
            currentSlotIndex = slotIndex;
            _targetSlotPos = slotWorldPos;
            _moveStartTime = Time.time;
        }

        private void Update()
        {
            float now = Time.time;

            switch (state)
            {
                case ShooterBlockState.MovingToSlot:
                    float elapsed = now - _moveStartTime;
                    float t = Mathf.Clamp01(elapsed / 0.28f);
                    // 平滑弧线插值
                    float arcY = Mathf.Sin(t * Mathf.PI) * 0.8f;
                    Vector3 cur = Vector3.Lerp(transform.position, _targetSlotPos, t);
                    transform.position = new Vector3(cur.x, cur.y + arcY, cur.z);

                    if (t >= 1.0f)
                    {
                        transform.position = _targetSlotPos;
                        state = ShooterBlockState.ActiveInSlot;
                        _nextFireTime = now + 0.1f;
                    }
                    break;

                case ShooterBlockState.ActiveInSlot:
                    HandleActiveShooting(now);
                    break;

                case ShooterBlockState.Disappearing:
                    // 缩小消失动画
                    transform.localScale = Vector3.Lerp(transform.localScale, Vector3.zero, Time.deltaTime * 15f);
                    if (transform.localScale.x < 0.05f)
                    {
                        _onEmptyCallback?.Invoke(this);
                        Destroy(gameObject);
                    }
                    break;
            }
        }

        private void HandleActiveShooting(float now)
        {
            if (_targetModel == null || remainingAmmo <= 0) return;

            if (now >= _nextFireTime)
            {
                // 寻找模型上当前暴露出来的【同色体素】
                if (_targetModel.FindNearestExposedVoxelOfColor(blockColor, transform.position, out Vector3Int hitGridPos, out Vector3 hitWorldPos))
                {
                    // 发射同色子弹
                    FireColorBullet(hitGridPos, hitWorldPos);
                    _nextFireTime = now + (1.0f / fireRate) + UnityEngine.Random.Range(-0.015f, 0.015f);
                }
                else
                {
                    // 暂无该颜色的暴露体素，稍作等待（等待模型旋转露出或其它颜色被消除）
                    _nextFireTime = now + 0.25f;
                }
            }
        }

        private void FireColorBullet(Vector3Int targetGridPos, Vector3 targetWorldPos)
        {
            // 创建并朝目标体素发射子弹
            GameObject bulletObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulletObj.name = "ColorBullet";
            bulletObj.transform.position = transform.position + Vector3.up * 0.6f;
            bulletObj.transform.localScale = Vector3.one * 0.25f;

            Collider c = bulletObj.GetComponent<Collider>();
            if (c != null) c.enabled = false;

            Material bm = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            bm.color = blockColor;
            bulletObj.GetComponent<Renderer>().sharedMaterial = bm;

            var bulletComp = bulletObj.AddComponent<ColorMatchBullet>();
            bulletComp.Launch(
                targetWorldPos,
                targetGridPos,
                blockColor,
                bulletSpeed,
                _targetModel,
                OnBulletHitSuccess
            );

            // 轻微后坐力抖动
            transform.position = _targetSlotPos - Vector3.up * 0.1f;
        }

        private void OnBulletHitSuccess()
        {
            remainingAmmo--;
            UpdateNumberDisplay();

            // 弹药耗尽消除逻辑
            if (remainingAmmo <= 0)
            {
                state = ShooterBlockState.Disappearing;
                // 播放消除特效
                if (VoxelDebrisManager.Instance != null)
                {
                    VoxelDebrisManager.Instance.SpawnDebris(transform.position, Vector3.up, blockColor, 0.2f, 8);
                }
            }
        }
    }

    /// <summary>
    /// 同色匹配子弹
    /// </summary>
    public class ColorMatchBullet : MonoBehaviour
    {
        private Vector3 _targetWorldPos;
        private Vector3Int _targetGridPos;
        private Color32 _bulletColor;
        private float _speed;
        private VoxelModelInstance _modelInstance;
        private Action _onHitCallback;
        private float _spawnTime;

        public void Launch(Vector3 targetWorldPos, Vector3Int targetGridPos, Color32 color, float speed, VoxelModelInstance model, Action onHit)
        {
            _targetWorldPos = targetWorldPos;
            _targetGridPos = targetGridPos;
            _bulletColor = color;
            _speed = speed;
            _modelInstance = model;
            _onHitCallback = onHit;
            _spawnTime = Time.time;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 cur = transform.position;
            Vector3 dir = (_targetWorldPos - cur);
            float dist = dir.magnitude;
            float step = _speed * dt;

            if (dist <= step || dist < 0.15f)
            {
                // 击中目标体素！
                if (_modelInstance != null)
                {
                    _modelInstance.ApplyColorDamage(_targetGridPos, 1, _bulletColor, _targetWorldPos, -dir.normalized);
                }
                _onHitCallback?.Invoke();
                Destroy(gameObject);
                return;
            }

            transform.position = cur + dir.normalized * step;

            if (Time.time - _spawnTime > 2.0f)
            {
                Destroy(gameObject);
            }
        }
    }
}
