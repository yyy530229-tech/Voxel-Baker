using System;
using UnityEngine;
using VoxelBaker.Data;
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
    /// 彩色射击方块单元 (1:1 绝对守恒：发射即扣弹并锁定对应体素，消尽优雅消失，绝不遗留残余数字！)
    /// </summary>
    public class VoxelColorShooterBlock : MonoBehaviour
    {
        [Header("方块属性")]
        public Color32 blockColor = new Color32(230, 40, 50, 255);
        public int initialCapacity = 50;
        public int remainingAmmo = 50;
        public float bulletSpeed = 56f;
        public float shotCooldown = 0.08f;

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
        private bool _isDisposed = false;

        public void Initialize(Color32 color, int capacity, VoxelModelInstance targetModel, Action<VoxelColorShooterBlock> onEmpty)
        {
            _transform = transform;
            blockColor = color;
            initialCapacity = capacity;
            remainingAmmo = capacity;
            _targetModel = targetModel;
            _onEmptyCallback = onEmpty;
            state = ShooterBlockState.InQueue;
            _isDisposed = false;

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

            // 3D 数字标签 (清晰显示剩余消除数量)
            Transform textChild = transform.Find("AmmoNumberText");
            if (textChild == null)
            {
                GameObject textObj = new GameObject("AmmoNumberText");
                textObj.transform.SetParent(transform);
                textObj.transform.localPosition = new Vector3(0f, 0.05f, -0.52f);
                textObj.transform.localRotation = Quaternion.identity;
                textObj.transform.localScale = Vector3.one * 0.14f;

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
            if (_isDisposed) return;

            float now = Time.time;

            switch (state)
            {
                case ShooterBlockState.MovingToSlot:
                    float elapsed = now - _moveStartTime;
                    float t = Mathf.Clamp01(elapsed / 0.28f);
                    float arcY = Mathf.Sin(t * Mathf.PI) * 0.8f;
                    Vector3 cur = Vector3.Lerp(transform.position, _targetSlotPos, t);
                    transform.position = new Vector3(cur.x, cur.y + arcY, cur.z);

                    if (t >= 1.0f)
                    {
                        transform.position = _targetSlotPos;
                        state = ShooterBlockState.ActiveInSlot;
                        _nextFireTime = now + 0.10f;
                    }
                    break;

                case ShooterBlockState.ActiveInSlot:
                    HandleRhythmicShooting(now);
                    break;

                case ShooterBlockState.Disappearing:
                    transform.localScale = Vector3.Lerp(transform.localScale, Vector3.zero, Time.deltaTime * 14f);
                    if (transform.localScale.x < 0.05f)
                    {
                        _isDisposed = true;
                        _onEmptyCallback?.Invoke(this);
                        Destroy(gameObject);
                    }
                    break;
            }
        }

        private void HandleRhythmicShooting(float now)
        {
            if (_targetModel == null || remainingAmmo <= 0 || _isDisposed) return;

            if (now >= _nextFireTime)
            {
                // 独占锁定 1 个最外层未被其它子弹锁定的【同色体素】
                if (_targetModel.FindAndReserveExposedVoxel(blockColor, transform.position, out Vector3Int hitGridPos, out Vector3 hitWorldPos))
                {
                    // 发射子弹并立即扣减 1 点弹药 (1发对1体素绝对守恒)
                    FireColorBullet(hitGridPos, hitWorldPos);

                    remainingAmmo--;
                    UpdateNumberDisplay();

                    if (remainingAmmo <= 0)
                    {
                        state = ShooterBlockState.Disappearing;
                        if (VoxelDebrisManager.Instance != null)
                        {
                            VoxelDebrisManager.Instance.SpawnDebris(transform.position, Vector3.up, blockColor, 0.25f, 6);
                        }
                    }

                    _nextFireTime = now + shotCooldown + UnityEngine.Random.Range(-0.008f, 0.008f);
                }
                else
                {
                    // 当前未露出同色外部体素，等待模型旋转露出
                    _nextFireTime = now + 0.15f;
                }
            }
        }

        private void FireColorBullet(Vector3Int targetGridPos, Vector3 targetWorldPos)
        {
            if (_isDisposed) return;

            Vector3 spawnPos = transform.position + Vector3.up * 0.6f;

            // 生成清晰透明白色弹道光轨
            CreateTrajectoryRay(spawnPos, targetWorldPos);

            // 发射高亮白色能量弹头
            GameObject bulletObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulletObj.name = "ColorBullet";
            bulletObj.transform.position = spawnPos;
            bulletObj.transform.localScale = Vector3.one * 0.22f;

            Collider c = bulletObj.GetComponent<Collider>();
            if (c != null) c.enabled = false;

            Material bm = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            bm.color = Color.white;
            bulletObj.GetComponent<Renderer>().sharedMaterial = bm;

            TrailRenderer trail = bulletObj.AddComponent<TrailRenderer>();
            trail.time = 0.08f;
            trail.startWidth = 0.12f;
            trail.endWidth = 0.01f;
            trail.sharedMaterial = bm;
            trail.startColor = new Color(1f, 1f, 1f, 0.95f);
            trail.endColor = new Color(1f, 1f, 1f, 0f);

            var bulletComp = bulletObj.AddComponent<ColorMatchBullet>();
            bulletComp.Launch(
                targetGridPos,
                blockColor,
                bulletSpeed,
                _targetModel
            );

            // 轻微后坐力弹跳
            transform.position = _targetSlotPos - Vector3.up * 0.05f;
        }

        private void CreateTrajectoryRay(Vector3 start, Vector3 end)
        {
            GameObject lineObj = new GameObject("TrajectoryRay");
            LineRenderer line = lineObj.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = 0.04f;
            line.endWidth = 0.015f;

            Material lm = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            lm.color = new Color(1f, 1f, 1f, 0.4f);
            line.sharedMaterial = lm;
            line.startColor = new Color(1f, 1f, 1f, 0.55f);
            line.endColor = new Color(1f, 1f, 1f, 0.1f);

            Destroy(lineObj, 0.12f);
        }
    }

    /// <summary>
    /// 同色匹配子弹 (独占命中，100% 必定击碎目标体素)
    /// </summary>
    public class ColorMatchBullet : MonoBehaviour
    {
        private Vector3Int _targetGridPos;
        private Color32 _bulletColor;
        private float _speed;
        private VoxelModelInstance _modelInstance;
        private float _spawnTime;

        public void Launch(Vector3Int targetGridPos, Color32 color, float speed, VoxelModelInstance model)
        {
            _targetGridPos = targetGridPos;
            _bulletColor = color;
            _speed = speed;
            _modelInstance = model;
            _spawnTime = Time.time;
        }

        private void Update()
        {
            if (_modelInstance == null || _modelInstance.Asset == null)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 localPos = _modelInstance.Asset.GridToLocalPosition(_targetGridPos);
            Vector3 currentTargetWorldPos = _modelInstance.transform.TransformPoint(localPos);

            float dt = Time.deltaTime;
            Vector3 cur = transform.position;
            Vector3 dir = (currentTargetWorldPos - cur);
            float dist = dir.magnitude;
            float step = _speed * dt;

            if (dist <= step || dist < 0.28f)
            {
                // 击中体素并解除锁定！
                _modelInstance.ApplyColorDamage(_targetGridPos, 1, _bulletColor, currentTargetWorldPos, -dir.normalized);
                _modelInstance.ReleaseTargetVoxel(_targetGridPos);

                Destroy(gameObject);
                return;
            }

            transform.position = cur + dir.normalized * step;

            if (Time.time - _spawnTime > 1.5f)
            {
                if (_modelInstance != null)
                {
                    _modelInstance.ApplyColorDamage(_targetGridPos, 1, _bulletColor, currentTargetWorldPos, Vector3.up);
                    _modelInstance.ReleaseTargetVoxel(_targetGridPos);
                }
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_modelInstance != null)
            {
                _modelInstance.ReleaseTargetVoxel(_targetGridPos);
            }
        }
    }
}
