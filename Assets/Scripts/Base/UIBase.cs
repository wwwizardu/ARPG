using UnityEngine;
using UnityEngine.EventSystems;

namespace ARPG.Base
{
    public class UIBase : MonoBehaviour
    {
        protected string _name;
        protected RectTransform _rectTransform;

        [SerializeField] protected bool _dontDestroy = false;
        [SerializeField] protected bool _isBase = false;
        [SerializeField] private AudioClip _openSound;
        [SerializeField] private AudioClip _closeSound;

        public string Name { get { return _name; } }
        public bool DontDestroy { get { return _dontDestroy; } }

        public bool IsBase { get { return _isBase; } }

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

        public virtual void Close(bool isDestroy = false)
        {
            // Hub.s.uiman.Close(_name, isDestroy);
        }

        // public virtual bool UpdateInput(InputController.BsnInput inInput)
        // {
        //     if(IsBase == false && inInput.UI.CloseUI.WasReleasedThisFrame() == true)
        //     {
        //         Close();
        //         return true;
        //     }

        //     return false;
        // }

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

        public virtual void OnOpen()
        {
            gameObject.SetActive(true);
            _rectTransform.SetAsLastSibling();

            //UpdateLocalization(DataManager.Instance.LocalizationIndex);

            //if (_openSound != null)
            //{
            //    RGM.Instance.AudioMgr.Play(AudioSourceType.Effect, _openSound);
            //}
        }

        public virtual void OnClose()
        {
            //if (_closeSound != null)
            //{
            //    RGM.Instance.AudioMgr.Play(AudioSourceType.Effect, _closeSound);
            //}
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

        //protected string GetLocalizedString(string inTableName, string inKey)
        //{
        //    return LocalizationSettings.StringDatabase.GetLocalizedString(inTableName, inKey, LocalizationSettings.SelectedLocale);
        //}
    }
}


