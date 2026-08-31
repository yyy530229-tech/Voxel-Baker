using GameFramework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace VoxelGameFramework.UI
{
    /// <summary>
    /// UI 组辅助器 (每个 UI 组一个 Canvas 根节点)
    /// </summary>
    public class UnityUIGroupHelper : IUIGroupHelper
    {
        private Transform _groupRoot;
        private Canvas _canvas;
        private CanvasScaler _scaler;

        public UnityUIGroupHelper(Transform groupRoot)
        {
            _groupRoot = groupRoot;

            // 每个 UI 组独立 Canvas (支持不同 sortingOrder)
            _canvas = groupRoot.gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 0;

            _scaler = groupRoot.gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1080, 1920);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 0.5f;
        }

        public void SetDepth(int depth)
        {
            if (_canvas != null)
            {
                _canvas.sortingOrder = depth;
            }
        }

        public Transform GroupRoot => _groupRoot;
    }

    /// <summary>
    /// UI 表单辅助器 (通过 Resources 加载 UIForm 预制体)
    /// </summary>
    public class UnityUIFormHelper : IUIFormHelper
    {
        public object InstantiateUIForm(object uiFormAsset)
        {
            if (uiFormAsset is GameObject prefab)
            {
                return Object.Instantiate(prefab);
            }
            return null;
        }

        public IUIForm CreateUIForm(object uiFormInstance, IUIGroup uiGroup, object userData)
        {
            if (uiFormInstance is GameObject go)
            {
                var uiForm = go.GetComponent<IUIForm>();
                if (uiForm == null)
                {
                    uiForm = go.AddComponent<UIFormLogic>();
                }
                return uiForm;
            }
            return null;
        }

        public void ReleaseUIForm(object uiFormAsset, object uiFormInstance)
        {
            if (uiFormInstance is GameObject go)
            {
                Object.Destroy(go);
            }
        }
    }
}
