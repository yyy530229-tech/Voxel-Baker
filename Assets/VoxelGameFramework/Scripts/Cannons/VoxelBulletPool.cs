using GameFramework;
using GameFramework.Event;
using GameFramework.ObjectPool;
using UnityEngine;
using VoxelBaker.Runtime;
using VoxelGameFramework.Core;
using VoxelGameFramework.Events;

namespace VoxelGameFramework.Cannons
{
    /// <summary>
    /// 子弹对象在 GameFramework 对象池中的包装体
    ///
    /// ObjectBase 的三个生命周期钩子:
    ///   OnSpawn   —— 从池取出时, 负责激活 GameObject
    ///   OnUnspawn —— 归还到池时, 负责隐藏 + 清理拖尾
    ///   Release   —— 真正销毁时 (超容量自动释放 / 关闭), 负责 Destroy
    /// </summary>
    public class ColorMatchBulletObject : ObjectBase
    {
        public ColorMatchBulletObject(string name, GameObject target)
        {
            if (target == null)
            {
                throw new GameFrameworkException("ColorMatchBulletObject 的 target 不能为 null。");
            }

            Initialize(name, target);
        }

        public GameObject BulletGo => (GameObject)Target;

        // 访问修饰符必须写 protected 而非 protected internal:
        // 基类成员是 protected internal, 但 GameFramework 位于独立的 GameFramework.asmdef
        // 程序集, 本文件在默认程序集 Assembly-CSharp。跨程序集时 protected internal 的
        // internal 部分对外不可见, 派生类只能看到 protected, 所以重写必须降为 protected,
        // 写 protected internal 会触发 CS0507。
        protected override void OnSpawn()
        {
            base.OnSpawn();

            GameObject go = BulletGo;
            if (go != null) go.SetActive(true);
        }

        protected override void OnUnspawn()
        {
            base.OnUnspawn();

            GameObject go = BulletGo;
            if (go == null) return;

            // 拖尾不清会在下次复用时从旧位置拉出一条飞线
            var trail = go.GetComponent<TrailRenderer>();
            if (trail != null) trail.Clear();

            // 保持在池节点下, 不打断层级; 位置由 SpawnBullet 重新指定
            go.SetActive(false);
        }

        protected override void Release(bool isShutdown)
        {
            GameObject go = BulletGo;
            if (go == null) return;

            // 写全 UnityEngine.Object: 本文件同时 using 了 GameFramework.ObjectPool,
            // 显式限定避免与任何同名类型产生 CS0104 歧义, 也让阅读时一眼看出是 Unity API。
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(go);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }

    /// <summary>
    /// 能量弹对象池 (基于 GameFramework IObjectPoolManager)
    ///
    /// 相比旧的手搓 Queue 实现, 换成 GF 对象池带来的实际收益:
    ///   1. 自动释放: 池内对象数超过 capacity 时, 空闲对象会被自动 Release 销毁,
    ///      不会再像旧实现那样无限增长到 poolSize*2 才砍一刀。
    ///   2. Locked / Priority: 可以钉住特定对象不被释放, 后续做 Boss 弹幕保底很方便。
    ///   3. 生命周期钩子 (OnSpawn/OnUnspawn/Release) 由框架调度, 不用在每个调用点手动 SetActive。
    ///   4. ReleaseAllUnused() 可在换关时一键回收, 控制内存峰值。
    ///
    /// 对外 API (SpawnBullet / DespawnBullet) 与旧版完全一致, ColorMatchBullet 无需改动。
    /// </summary>
    public class VoxelBulletPool : MonoBehaviour
    {
        private const string PoolName = "VoxelBulletPool";

        // 池对象分桶名。GF 的 ObjectPool.Register 用 obj.Name 分桶, Spawn(name) 用同一 name 查,
        // 两端必须一致。Register 时 ColorMatchBulletObject 的 Name 即此常量, Spawn 也传此常量,
        // 因此分桶命中是正确的。子弹发不出来的真正元凶不是分桶, 而是下面的订阅时序竞态。
        private const string BulletObjectName = "VoxelBullet";

        [Header("池配置")]
        [Tooltip("预热对象数量。池内对象数超过这个值后, 空闲对象才会被自动释放")]
        public int poolSize = 120;
        [Tooltip("自动释放检查间隔 (秒)")]
        public float autoReleaseInterval = 5f;
        [Tooltip("对象过期秒数。设为 float.MaxValue 表示只按容量回收, 不做时间过期")]
        public float expireTime = float.MaxValue;

        public GameObject bulletPrefab;

        private IObjectPool<ColorMatchBulletObject> _pool;
        private Material _bulletMaterial;
        private int _createdCount = 0;

        /// <summary>对象池是否已就绪</summary>
        public bool IsReady => _pool != null;

        private void Start()
        {
            ServiceLocator.Register(this);
            // 若本 Start 早于 GameFrameworkEntryComponent.Bootstrap 执行, GameEventBus 会缓存此订阅,
            // 待 Bootstrap 完成后统一补订 (见 GameEventBus.FlushPending), 不会丢失。
            GameEventBus.Subscribe(BulletFiredEventArgs.EventId, OnBulletRequested);

            TryCreatePool();
        }

        private void Update()
        {
            // 自愈重试: 与 VoxelUIManager 同理, 启动首帧 GameFramework 可能还没 Bootstrap 完
            if (_pool == null) TryCreatePool();
        }

        private void OnDestroy()
        {
            GameEventBus.Unsubscribe(BulletFiredEventArgs.EventId, OnBulletRequested);

            // 整池释放会销毁所有对象 (含正在飞行的), 场景卸载时无需额外处理
            if (_pool != null)
            {
                _pool.ReleaseAllUnused();
                _pool = null;
            }
        }

        private void TryCreatePool()
        {
            if (_pool != null) return;
            if (GameFrameworkEntryComponent.Instance == null ||
                !GameFrameworkEntryComponent.Instance.IsInitialized)
            {
                return;
            }

            var objectPoolManager = GameFrameworkEntry.GetModule<IObjectPoolManager>();
            if (objectPoolManager == null) return;

            // 场景重载时可能残留同名池, 直接复用而不是重复创建
            if (objectPoolManager.HasObjectPool<ColorMatchBulletObject>(PoolName))
            {
                _pool = objectPoolManager.GetObjectPool<ColorMatchBulletObject>(PoolName);
            }
            else
            {
                _pool = objectPoolManager.CreateSingleSpawnObjectPool<ColorMatchBulletObject>(
                    PoolName, autoReleaseInterval, poolSize, expireTime, 0);
            }

            Prewarm();
        }

        private void Prewarm()
        {
            _bulletMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            _bulletMaterial.color = Color.white;

            for (int i = 0; i < poolSize; i++)
            {
                var bulletObject = CreateBulletObject($"PooledBullet_{i}");
                if (bulletObject != null) _pool.Register(bulletObject, false);
            }

            Debug.Log($"[VoxelBulletPool] GameFramework 对象池已就绪, 预热 {poolSize} 发子弹 (容量上限 {poolSize})");
        }

        private ColorMatchBulletObject CreateBulletObject(string name)
        {
            GameObject bulletObj;
            if (bulletPrefab != null)
            {
                bulletObj = Instantiate(bulletPrefab);
            }
            else
            {
                bulletObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bulletObj.name = name;
                bulletObj.transform.localScale = Vector3.one * 0.20f;
                bulletObj.GetComponent<Collider>().enabled = false;
                bulletObj.GetComponent<Renderer>().sharedMaterial = _bulletMaterial;

                // 拖尾
                var trail = bulletObj.AddComponent<TrailRenderer>();
                trail.time = 0.08f;
                trail.startWidth = 0.10f;
                trail.endWidth = 0.01f;
                trail.sharedMaterial = _bulletMaterial;
                trail.startColor = new Color(1f, 1f, 1f, 0.9f);
                trail.endColor = new Color(1f, 1f, 1f, 0f);
            }

            var comp = bulletObj.GetComponent<ColorMatchBullet>();
            if (comp == null) comp = bulletObj.AddComponent<ColorMatchBullet>();

            bulletObj.transform.SetParent(transform);
            bulletObj.SetActive(false);

            _createdCount++;
            // 统一注册名, 保证 _pool.Spawn(BulletObjectName) 能命中。
            // 参数 name 只用于 Hierarchy 显示, 不参与池检索。
            return new ColorMatchBulletObject(BulletObjectName, bulletObj);
        }

        /// <summary>
        /// 从池中获取子弹并发射
        /// </summary>
        public ColorMatchBullet SpawnBullet(Vector3 spawnPos, Vector3Int targetGridPos, Color32 color, float speed, VoxelModelInstance model)
        {
            if (_pool == null)
            {
                // 命令事件可能早于池创建到达。这里兜底直接生成一发, 而不是静默丢弃子弹 ——
                // 否则改为事件总线后, 启动时序竞态会导致前几发子弹全部消失、消除无法触发。
                GameObject fallbackObj;
                if (bulletPrefab != null)
                {
                    fallbackObj = Instantiate(bulletPrefab);
                }
                else
                {
                    fallbackObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    fallbackObj.transform.localScale = Vector3.one * 0.20f;
                    fallbackObj.GetComponent<Collider>().enabled = false;
                }
                fallbackObj.transform.position = spawnPos;
                fallbackObj.transform.rotation = Quaternion.identity;
                var fallbackBullet = fallbackObj.GetComponent<ColorMatchBullet>();
                if (fallbackBullet == null) fallbackBullet = fallbackObj.AddComponent<ColorMatchBullet>();
                fallbackBullet.Launch(targetGridPos, color, speed, model);
                return fallbackBullet;
            }

            // 池内对象全部在用 → 动态扩容, 保持旧行为 (绝不阻塞射击)
            if (!_pool.CanSpawn(BulletObjectName))
            {
                var extra = CreateBulletObject($"PooledBullet_Extra_{Time.frameCount}");
                if (extra != null) _pool.Register(extra, false);
            }

            ColorMatchBulletObject bulletObject = _pool.Spawn(BulletObjectName);
            if (bulletObject == null || bulletObject.BulletGo == null)
            {
                return null;
            }

            GameObject bulletObj = bulletObject.BulletGo;
            bulletObj.transform.SetParent(transform);
            bulletObj.transform.position = spawnPos;
            bulletObj.transform.rotation = Quaternion.identity;

            var bullet = bulletObj.GetComponent<ColorMatchBullet>();
            if (bullet == null) bullet = bulletObj.AddComponent<ColorMatchBullet>();

            bullet.Launch(targetGridPos, color, speed, model);
            return bullet;
        }

        /// <summary>
        /// 回收子弹到池中
        /// </summary>
        public void DespawnBullet(GameObject bulletObj)
        {
            if (bulletObj == null || _pool == null) return;

            // 传 target (GameObject) 给池, 由 OnUnspawn 统一处理隐藏与拖尾清理。
            // 超出容量的回收由 ObjectPoolManager 每 autoReleaseInterval 秒自动做一次,
            // 不必在这里逐发触发全池扫描。
            _pool.Unspawn(bulletObj);
        }

        /// <summary>
        /// BulletFiredEventArgs 命令事件处理器: 解包请求并执行 Spawn/Despawn。
        /// </summary>
        private void OnBulletRequested(object sender, GameEventArgs e)
        {
            var args = (BulletFiredEventArgs)e;
            if (args.Kind == BulletFiredEventArgs.RequestType.Spawn)
            {
                Debug.Log($"[VoxelBulletPool] 收到子弹生成事件 (pool={( _pool != null ? "ready" : "null")})");
                // 防御: 命令事件在下一帧分发, 而本池在 Start 里才创建。
                // 若 GameFramework 初始化时序导致事件先到、池尚未就绪, 先尝试补建,
                // 避免子弹被 SpawnBullet 内部 "_pool == null" 静默丢弃 (原来同步调用时无此问题)。
                if (_pool == null) TryCreatePool();
                SpawnBullet(args.SpawnPos, args.TargetGridPos, args.Color, args.Speed, args.Model);
            }
            else
            {
                DespawnBullet(args.BulletObject);
            }
        }
    }
}
