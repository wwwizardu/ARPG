using UnityEngine;
using UnityEngine.EventSystems;

namespace ARPG.Base
{
    public class UIBase : MonoBehaviour
    {
        protected string _name;
        protected RectTransform _rectTransform;

        public string Name { get { return _name; } }
        
        private int _localizationCodeIndex = 0;

        protected virtual void Awake()
        {
            //LocalizationSettings.SelectedLocaleChanged += OnSetLocalizedText;
        }

        protected virtual void OnDestroy()
        {
            //LocalizationSettings.SelectedLocaleChanged -= OnSetLocalizedText;
        }

        public virtual void Initialize(string inName, bool isForm = false)
        {
            _name = inName;
            _rectTransform = GetComponent<RectTransform>();
        }

        public virtual bool OnPointerDown(PointerEventData inEventData)
        {
            return true;
        }

        public virtual bool OnPointerUp(PointerEventData inEventData)
        {
            return true;
        }

        public virtual void OnPointerPressed(Vector3 inPosition)
        {

        }

        public virtual void CancelPointerEvent()
        {

        }

        public virtual bool OnDrag(PointerEventData eventData)
        {
            return true;
        }

        public virtual bool CheckClose()
        {
            return true;
        }



        // public virtual void OnBeginDragAndDrop(SlotUI inFromSlot)
        // {

        // }

        public virtual void OnEndDragAndDrop()
        {

        }

        public void UpdateLocalization(int inLocalizationCodeIndex)
        {
            if (_localizationCodeIndex == inLocalizationCodeIndex)
                return;

            _localizationCodeIndex = inLocalizationCodeIndex;
            OnUpdateLocalization();
        }

        protected virtual void OnUpdateLocalization()
        {

        }

        protected virtual async void LoadUI(string inName, Transform inParent)
        {
            // Addressables를 사용하여 UI 프리팹을 비동기적으로 로드
            var handle = UnityEngine.AddressableAssets.Addressables.InstantiateAsync(inName, inParent);
            await handle.Task;
            
            if (handle.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[UIBase] Failed to load UI: {inName}");
            }
        }

        //protected string GetLocalizedString(string inTableName, string inKey)
        //{
        //    return LocalizationSettings.StringDatabase.GetLocalizedString(inTableName, inKey, LocalizationSettings.SelectedLocale);
        //}
    }
}


