using System;
using UnityEngine;
using VoxelBaker.Runtime;
using VoxelGameFramework.Core;
using VoxelGameFramework.Events;
using VoxelGameFramework.Projectile;

namespace VoxelGameFramework.Cannons
{
    /// <summary>
    /// 单个射击炮台单元（匹配参考图底部带数字等级的粉色/彩色小方块炮台）
    /// </summary>
    public class VoxelCannonUnit : MonoBehaviour
    {
        [Header("炮台属性")]
        public int cannonIndex = 0;
        public int power = 33;            // 炮台数值等级（如 33, 45, 55, 66, 77）
        public float fireRate = 10f;      // 每秒射击次数
        public float bulletSpeed = 26f;
        public Color cannonColor = new Color(0.95f, 0.22f, 0.62f);

        [Header("后坐力动画")]
        public float recoilStrength = 0.15f;
        public float recoilRecoverySpeed = 12f;

        private Transform _bodyTransform;
        private Vector3 _originalLocalPos;
        private float _nextFireTime = 0f;
        private TextMesh _numberTextMesh;

        public void Initialize(int index, int initialPower, Color color)
        {
            cannonIndex = index;
            power = initialPower;
            cannonColor = color;

            _bodyTransform = transform;
            _originalLocalPos = _bodyTransform.localPosition;

            SetupVisuals();
        }

        private void SetupVisuals()
        {
            // 确保材质颜色
            Renderer r = GetComponent<Renderer>();
            if (r != null)
            {
                Material m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                m.color = cannonColor;
                r.sharedMaterial = m;
            }

            // 创建数字显示 (3D TextMesh)
            Transform textChild = transform.Find("CannonNumberText");
            if (textChild == null)
            {
                GameObject textObj = new GameObject("CannonNumberText");
                textObj.transform.SetParent(transform);
                textObj.transform.localPosition = new Vector3(0f, 0.05f, -0.52f);
                textObj.transform.localRotation = Quaternion.identity;
                textObj.transform.localScale = Vector3.one * 0.12f;

                _numberTextMesh = textObj.AddComponent<TextMesh>();
                _numberTextMesh.alignment = TextAlignment.Center;
                _numberTextMesh.anchor = TextAnchor.MiddleCenter;
                _numberTextMesh.fontSize = 36;
                _numberTextMesh.fontStyle = FontStyle.Bold;
                _numberTextMesh.color = Color.white;
            }
            else
            {
                _numberTextMesh = textChild.GetComponent<TextMesh>();
            }

            UpdateNumberDisplay();
        }

        public void UpdateNumberDisplay()
        {
            if (_numberTextMesh != null)
            {
                _numberTextMesh.text = power.ToString();
            }
        }

        public void UpgradePower(int delta)
        {
            power += delta;
            UpdateNumberDisplay();

            GameEventBus.Fire(this, CannonUpgradedEventArgs.Create(cannonIndex, power));
        }

        public void Tick(float now, Vector3 aimTargetPos, VoxelModelInstance targetModel, Action<Vector3, Vector3, int, float, VoxelModelInstance> spawnBulletAction)
        {
            // 恢复后坐力
            if (_bodyTransform != null)
            {
                _bodyTransform.localPosition = Vector3.Lerp(_bodyTransform.localPosition, _originalLocalPos, Time.deltaTime * recoilRecoverySpeed);
            }

            if (now >= _nextFireTime && targetModel != null)
            {
                Fire(aimTargetPos, targetModel, spawnBulletAction);
                _nextFireTime = now + (1.0f / fireRate) + UnityEngine.Random.Range(-0.02f, 0.02f);
            }
        }

        private void Fire(Vector3 aimTargetPos, VoxelModelInstance targetModel, Action<Vector3, Vector3, int, float, VoxelModelInstance> spawnBulletAction)
        {
            Vector3 spawnPos = transform.position + Vector3.up * 0.6f + Vector3.forward * UnityEngine.Random.Range(-0.05f, 0.05f);

            // 射击方向（稍带微量发散角）
            Vector3 aimDir = (aimTargetPos + UnityEngine.Random.insideUnitSphere * 0.45f - spawnPos).normalized;

            // 触发后坐力
            if (_bodyTransform != null)
            {
                _bodyTransform.localPosition = _originalLocalPos - Vector3.up * recoilStrength;
            }

            spawnBulletAction?.Invoke(spawnPos, aimDir, Mathf.Max(1, power / 20), bulletSpeed, targetModel);
        }
    }
}
