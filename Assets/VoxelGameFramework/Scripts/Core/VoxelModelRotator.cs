using UnityEngine;

namespace VoxelGameFramework.Core
{
    /// <summary>
    /// 体素目标模型 3D 旋转与浮动控制器
    /// 支持自动平滑自转、轻微呼吸浮动、以及玩家手指/鼠标交互拖拽 3D 旋转！
    /// </summary>
    public class VoxelModelRotator : MonoBehaviour
    {
        [Header("自转控制")]
        public bool autoRotate = true;
        public float rotateSpeed = 22f; // 每秒旋转度数 (Y轴)

        [Header("轻微浮动呼吸动画")]
        public bool enableBobbing = true;
        public float bobbingSpeed = 1.8f;
        public float bobbingAmplitude = 0.12f;

        [Header("玩家触控/鼠标交互旋转")]
        public bool allowDragToRotate = true;
        public float dragSensitivity = 0.45f;
        public float damping = 6.0f;

        private Vector3 _basePosition;
        private Vector3 _lastMousePos;
        private bool _isDragging = false;
        private float _angularVelocityY = 0f;

        private void Start()
        {
            _basePosition = transform.position;
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // 1. 鼠标/触控拖拽交互旋转 (按住右键或非UI区域左键拖拽)
            if (allowDragToRotate)
            {
                if (Input.GetMouseButtonDown(1) || (Input.GetMouseButtonDown(0) && Input.mousePosition.y > Screen.height * 0.25f))
                {
                    _isDragging = true;
                    _lastMousePos = Input.mousePosition;
                }
                else if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1))
                {
                    _isDragging = false;
                }

                if (_isDragging)
                {
                    Vector3 delta = Input.mousePosition - _lastMousePos;
                    _angularVelocityY = -delta.x * dragSensitivity * 50f;
                    _lastMousePos = Input.mousePosition;
                }
            }

            // 2. 应用角速度与自动旋转
            if (autoRotate && !_isDragging)
            {
                transform.Rotate(Vector3.up, rotateSpeed * dt, Space.World);
            }
            else if (Mathf.Abs(_angularVelocityY) > 0.01f)
            {
                transform.Rotate(Vector3.up, _angularVelocityY * dt, Space.World);
                _angularVelocityY = Mathf.Lerp(_angularVelocityY, 0f, dt * damping);
            }

            // 3. 上下微幅浮动呼吸感
            if (enableBobbing)
            {
                float newY = _basePosition.y + Mathf.Sin(Time.time * bobbingSpeed) * bobbingAmplitude;
                transform.position = new Vector3(_basePosition.x, newY, _basePosition.z);
            }
        }
    }
}
