using ARPG.Base;
using UnityEngine;

namespace ARPG.Base
{
    public class UIBaseForm : UIBase
    {
        [SerializeField] protected bool _dontDestroy = false;
        [SerializeField] private AudioClip _openSound;
        [SerializeField] private AudioClip _closeSound;
        [SerializeField] protected Transform _visual;
        [SerializeField] protected bool _isBase = false;


        public bool DontDestroy { get { return _dontDestroy; } }
        public bool IsBase { get { return _isBase; } }

        public virtual bool UpdateInput(Input.ArpgInput inInput)
        {
            if (IsBase == false && inInput.UI.Cancel.WasReleasedThisFrame() == true)
            {
                Close();
                return true;
            }

            return false;
        }

        public virtual void Close(bool isDestroy = false)
        {
            AR.s.UI.Close(_name, isDestroy);
        } 

        public virtual void OnOpen()
        {
            gameObject.SetActive(true);
            if (_rectTransform != null)
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
    }
}

