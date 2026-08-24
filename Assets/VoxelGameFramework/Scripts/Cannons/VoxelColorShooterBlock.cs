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
    /// 彩色射击方块单元 (匹配参考图2: 逐发节奏射击、视线射线首层命中、弹药归零优雅消失)
    /// </summary>
    public class VoxelColorShooterBlock : MonoBehaviour
    {
        [Header("方块属性")]
        public Color32 blockColor = new Color32(230, 40, 50, 255);
        public int initialCapacity = 50;
        public int remainingAmmo = 50;
        public float bulletSpeed = 22f;
        public float shotCooldown = 0.18f; // 逐发有节奏发射，而不是瞬间机枪乱射

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
                        _nextFireTime = now + 0.15f;
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
                // 通过视线射线寻找最外层暴露的【同色体素】（绝不隔空击穿，先打外层）
                if (FindOutermostExposedVoxelOfColor(out Vector3Int hitGridPos, out Vector3 hitWorldPos))
                {
                    FireColorBullet(hitGridPos, hitWorldPos);
                    _nextFireTime = now + shotCooldown + UnityEngine.Random.Range(-0.02f, 0.02f);
                }
                else
                {
                    // 当前未露出同色体素，等待模型旋转露出
                    _nextFireTime = now + 0.25f;
                }
            }
        }

        private bool FindOutermostExposedVoxelOfColor(out Vector3Int hitGridPos, out Vector3 hitWorldPos)
        {
            hitGridPos = Vector3Int.zero;
            hitWorldPos = Vector3.zero;

            if (_targetModel == null || _targetModel.Asset == null) return false;

            // 1. 获取所有该颜色的表面可见体素
            return _targetModel.FindExposedVoxelOfColor(blockColor, transform.position, out hitGridPos, out hitWorldPos);
        }

        private void FireColorBullet(Vector3Int targetGridPos, Vector3 targetWorldPos)
        {
            if (_isDisposed) return;

            GameObject bulletObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulletObj.name = "ColorBullet";
            bulletObj.transform.position = transform.position + Vector3.up * 0.55f;
            bulletObj.transform.localScale = Vector3.one * 0.24f;

            Collider c = bulletObj.GetComponent<Collider>();
            if (c != null) c.enabled = false;

            Material bm = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            bm.color = Color.Lerp(blockColor, Color.white, 0.4f);
            bulletObj.GetComponent<Renderer>().sharedMaterial = bm;

            TrailRenderer trail = bulletObj.AddComponent<TrailRenderer>();
            trail.time = 0.2f;
            trail.startWidth = 0.2f;
            trail.endWidth = 0.02f;
            trail.sharedMaterial = bm;
            trail.startColor = new Color(1f, 1f, 1f, 0.9f);
            trail.endColor = new Color(blockColor.r / 255f, blockColor.g / 255f, blockColor.b / 255f, 0f);

            var bulletComp = bulletObj.AddComponent<ColorMatchBullet>();
            bulletComp.Launch(
                targetWorldPos,
                targetGridPos,
                blockColor,
                bulletSpeed,
                _targetModel,
                this
            );

            // 轻微后坐力弹跳
            transform.position = _targetSlotPos - Vector3.up * 0.08f;
        }

        public void OnBulletHitSuccess()
        {
            if (_isDisposed || state == ShooterBlockState.Disappearing) return;

            remainingAmmo--;
            UpdateNumberDisplay();

            if (remainingAmmo <= 0)
            {
                state = ShooterBlockState.Disappearing;
                if (VoxelDebrisManager.Instance != null)
                {
                    VoxelDebrisManager.Instance.SpawnDebris(transform.position, Vector3.up, blockColor, 0.25f, 8);
                }
            }
        }
    }

    /// <summary>
    /// 同色匹配子弹 (安全回调引用，杜绝 MissingReferenceException)
    /// </summary>
    public class ColorMatchBullet : MonoBehaviour
    {
        private Vector3 _targetWorldPos;
        private Vector3Int _targetGridPos;
        private Color32 _bulletColor;
        private float _speed;
        private VoxelModelInstance _modelInstance;
        private VoxelColorShooterBlock _sourceBlock;
        private float _spawnTime;

        public void Launch(Vector3 targetWorldPos, Vector3Int targetGridPos, Color32 color, float speed, VoxelModelInstance model, VoxelColorShooterBlock sourceBlock)
        {
            _targetWorldPos = targetWorldPos;
            _targetGridPos = targetGridPos;
            _bulletColor = color;
            _speed = speed;
            _modelInstance = model;
            _sourceBlock = sourceBlock;
            _spawnTime = Time.time;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 cur = transform.position;
            Vector3 dir = (_targetWorldPos - cur);
            float dist = dir.magnitude;
            float step = _speed * dt;

            if (dist <= step || dist < 0.22f)
            {
                // 击中体素
                if (_modelInstance != null)
                {
                    _modelInstance.ApplyColorDamage(_targetGridPos, 1, _bulletColor, _targetWorldPos, -dir.normalized);
                }

                if (_sourceBlock != null)
                {
                    _sourceBlock.OnBulletHitSuccess();
                }

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
