using System;
using UnityEngine;
using VoxelBaker.Data;
using VoxelBaker.Runtime;
using VoxelGameFramework.Core;
using VoxelGameFramework.Events;

namespace VoxelGameFramework.Projectile
{
    public class VoxelBullet : MonoBehaviour
    {
        [Header("子弹属性")]
        public float speed = 25f;
        public int damage = 1;
        public float maxLifeTime = 2.5f;

        private Vector3 _velocity;
        private float _spawnTime;
        private VoxelModelInstance _targetInstance;
        private Action<VoxelBullet> _onRecycleCallback;
        private bool _isAlive = false;

        public void Launch(
            Vector3 startPos,
            Vector3 direction,
            int bulletDamage,
            float bulletSpeed,
            VoxelModelInstance target,
            Action<VoxelBullet> onRecycle)
        {
            transform.position = startPos;
            _velocity = direction.normalized * bulletSpeed;
            damage = bulletDamage;
            speed = bulletSpeed;
            _targetInstance = target;
            _onRecycleCallback = onRecycle;
            _spawnTime = Time.time;
            _isAlive = true;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!_isAlive) return;

            float dt = Time.deltaTime;
            Vector3 curPos = transform.position;
            Vector3 stepMove = _velocity * dt;
            Vector3 nextPos = curPos + stepMove;
            float stepDistance = stepMove.magnitude;

            // 进行连续射线 DDA 检测，避免高速穿透
            if (_targetInstance != null && _targetInstance.Asset != null)
            {
                Ray ray = new Ray(curPos, _velocity.normalized);
                if (_targetInstance.Raycast(ray, out VoxelRaycastHit hit, stepDistance + 0.15f))
                {
                    // 击中体素！
                    _targetInstance.ApplyDamage(hit.gridPos, damage, hit.worldHitPoint, hit.hitNormal);

                    // 广播事件 (走 GameFramework 事件总线)
                    GameEventBus.Fire(this, VoxelDamagedEventArgs.Create(hit.worldHitPoint, damage, 0));

                    Recycle();
                    return;
                }
            }

            transform.position = nextPos;

            // 超时或飞出视野销毁
            if (Time.time - _spawnTime > maxLifeTime || nextPos.y > 18f)
            {
                Recycle();
            }
        }

        private void Recycle()
        {
            if (!_isAlive) return;
            _isAlive = false;
            gameObject.SetActive(false);
            _onRecycleCallback?.Invoke(this);
        }
    }
}
