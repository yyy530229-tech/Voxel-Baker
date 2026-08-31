using GameFramework.UI;
using UnityEngine;

namespace VoxelGameFramework.UI
{
    /// <summary>
    /// uGUI 界面逻辑基类 (实现 GameFramework IUIForm)
    /// 子类挂到 UI Prefab 上, 通过生命周期回调管理界面
    /// </summary>
    public class UIFormLogic : MonoBehaviour, IUIForm
    {
        public int SerialId { get; private set; }
        public string UIFormAssetName { get; private set; }
        public object Handle => gameObject;
        public IUIGroup UIGroup { get; private set; }
        public int DepthInUIGroup { get; private set; }
        public bool PauseCoveredUIForm { get; private set; }

        protected virtual void OnUpdateForm(float elapseSeconds, float realElapseSeconds) { }

        #region IUIForm Implementation
        public virtual void OnInit(int serialId, string uiFormAssetName, IUIGroup uiGroup,
            bool pauseCoveredUIForm, bool isNewInstance, object userData)
        {
            SerialId = serialId;
            UIFormAssetName = uiFormAssetName;
            UIGroup = uiGroup;
            PauseCoveredUIForm = pauseCoveredUIForm;
        }

        public virtual void OnRecycle() { }

        public virtual void OnOpen(object userData) { }

        public virtual void OnClose(bool isShutdown, object userData) { }

        public virtual void OnPause() { }

        public virtual void OnResume() { }

        public virtual void OnCover() { }

        public virtual void OnReveal() { }

        public virtual void OnRefocus(object userData) { }

        public void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            OnUpdateForm(elapseSeconds, realElapseSeconds);
        }

        public void OnDepthChanged(int uiGroupDepth, int depthInUIGroup)
        {
            DepthInUIGroup = depthInUIGroup;
            transform.SetSiblingIndex(depthInUIGroup);
        }
        #endregion
    }
}
