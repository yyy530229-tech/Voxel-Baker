using System;
using UnityEngine;
using VoxelBaker.Data;
using VoxelBaker.Runtime;
using VoxelGameFramework.Audio;
using VoxelGameFramework.Core;
using VoxelGameFramework.Events;

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
    /// 彩色射击方块单元 (Voxel Color Shooter Block)
    /// 职责：
    /// 1. 承载特定色系的消除任务与弹药计数；
    /// 2. 在活动槽位中以快节奏节拍发射追踪能量弹；
    /// 3. 弹药耗尽后播放消散特效并释放槽位，1:1 绝对守恒消除。
    /// </summary>
    public class VoxelColorShooterBlock : MonoBehaviour
    {
        [Header("方块属性")]
        public Color32 blockColor = new Color32(230, 40, 50, 255);
        public int initialCapacity = 50;
        public int remainingAmmo = 50;
        public float bulletSpeed = 58f;
        public float shotCooldown = 0.075f;

        [Header("状态")]
        public ShooterBlockState state = ShooterBlockState.InQueue;
        public int currentSlotIndex = -1;

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

            // 3D 彩色方块视觉 (参考图底部带数字的立体方块)
            if (_renderer != null)
            {
                _renderer.enabled = true;
                Material m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                _renderer.sharedMaterial = m;
                _propBlock.SetColor("_BaseColor", blockColor);
                _propBlock.SetColor("_Color", blockColor);
                _renderer.SetPropertyBlock(_propBlock);
            }

            // 3D 数字标签 (参考图方块正面的数字)
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
            }
            else
            {
                _numberText = textChild.GetComponent<TextMesh>();
            }

            // 根据底色明度自适应切换黑字/白字
            float lum = VoxelColorUtility.GetLuminance(blockColor);
            if (_numberText != null)
            {
                _numberText.color = lum > 0.55f ? new Color(0.12f, 0.12f, 0.15f, 1f) : Color.white;
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
                // 独占锁定 1 个最外层未被锁定的目标体素
                if (_targetModel.FindAndReserveExposedVoxel(blockColor, transform.position, out Vector3Int hitGridPos, out Vector3 hitWorldPos))
                {
                    FireColorBullet(hitGridPos, hitWorldPos);

                    // 射击音效 (改发命令事件, 由 VoxelSoundManager 订阅执行)
                    GameEventBus.Fire(this, SfxPlayedEventArgs.Create(
                        VoxelSoundManager.SfxType.Shoot, 0.35f));

                    remainingAmmo--;
                    UpdateNumberDisplay();

                    if (remainingAmmo <= 0)
                    {
                        state = ShooterBlockState.Disappearing;
                        // 碎片爆裂 (改发命令事件, 由 VoxelDebrisManager 订阅执行)
                        GameEventBus.Fire(this, DebrisSpawnedEventArgs.Create(
                            transform.position, Vector3.up, blockColor, 0.25f, 6));
                    }

                    _nextFireTime = now + shotCooldown + UnityEngine.Random.Range(-0.005f, 0.005f);
                }
                else
                {
                    // 等待模型旋转露出同色体素
                    _nextFireTime = now + 0.12f;
                }
            }
        }

        private void FireColorBullet(Vector3Int targetGridPos, Vector3 targetWorldPos)
        {
            if (_isDisposed) return;

            Vector3 spawnPos = transform.position + Vector3.up * 0.55f;

            // 从对象池获取子弹 (改发命令事件, 由 VoxelBulletPool 订阅执行)
            var pool = ServiceLocator.Get<VoxelBulletPool>();
            if (pool != null)
            {
                GameEventBus.Fire(this, BulletFiredEventArgs.CreateSpawn(
                    spawnPos, targetGridPos, blockColor, bulletSpeed, _targetModel));
            }
            else
            {
                // 兜底: 池不存在时直接创建
                GameObject bulletObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bulletObj.name = "ColorMatchBullet";
                bulletObj.transform.position = spawnPos;
                bulletObj.transform.localScale = Vector3.one * 0.20f;
                bulletObj.GetComponent<Collider>().enabled = false;

                Material bm = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
                bm.color = Color.white;
                bulletObj.GetComponent<Renderer>().sharedMaterial = bm;

                var bulletComp = bulletObj.AddComponent<ColorMatchBullet>();
                bulletComp.Launch(targetGridPos, blockColor, bulletSpeed, _targetModel);
            }

            // 轻微后坐力回弹
            transform.position = _targetSlotPos - Vector3.up * 0.04f;
        }
    }

    /// <summary>
    /// 同色精准匹配子弹 (Color Match Bullet)
    /// </summary>
    public class ColorMatchBullet : MonoBehaviour
    {
        private Vector3Int _targetGridPos;
        private Color32 _bulletColor;
        private float _speed;
        private VoxelModelInstance _modelInstance;
        private float _spawnTime;
        private static int _diagHitCount = 0; // [DIAG] 临时诊断计数器, 确认后删除

        public void Launch(Vector3Int targetGridPos, Color32 color, float speed, VoxelModelInstance model)
        {
            _targetGridPos = targetGridPos;
            _bulletColor = color;
            _speed = speed;
            _modelInstance = model;
            _spawnTime = Time.time;
            // 对象池复用: 进入新一轮飞行前解除回收锁并重新激活
            _returnedToPool = false;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (_modelInstance == null || _modelInstance.Asset == null)
            {
                ReturnToPool();
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
                // [DIAG] 临时诊断: 只打前 5 次, 确认子弹确实击中并触发消除。确认后删除。
                if (_diagHitCount < 5)
                {
                    _diagHitCount++;
                    UnityEngine.Debug.Log($"[DIAG][BulletHit] 命中! gridPos={_targetGridPos} dist={dist:F2} aliveBefore={_modelInstance.IsVoxelAlive(_targetGridPos)}");
                }
                // 击中体素并释放目标
                _modelInstance.ApplyColorDamage(_targetGridPos, 1, _bulletColor, currentTargetWorldPos, -dir.normalized);
                _modelInstance.ReleaseTargetVoxel(_targetGridPos);

                ReturnToPool();
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
                ReturnToPool();
            }
        }

        private bool _returnedToPool = false;

        /// <summary>
        /// 回收子弹到对象池 (替代 Destroy)
        /// </summary>
        private void ReturnToPool()
        {
            if (_returnedToPool) return;
            _returnedToPool = true;

            if (_modelInstance != null)
            {
                _modelInstance.ReleaseTargetVoxel(_targetGridPos);
            }

            if (ServiceLocator.Get<VoxelBulletPool>() != null)
            {
                GameEventBus.Fire(this, BulletFiredEventArgs.CreateDespawn(gameObject));
            }
            else
            {
                Destroy(gameObject);
            }

            // 回收事件是延迟到下一帧才分发的, 这里必须立刻停用自身:
            // 否则这一帧到下一帧之间 Update 仍会执行, 此时 _modelInstance 已被置空,
            // 会再次走进 ReturnToPool 并重复 Unspawn 同一个池对象。
            _modelInstance = null;
            _targetGridPos = Vector3Int.zero;
            gameObject.SetActive(false);
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
