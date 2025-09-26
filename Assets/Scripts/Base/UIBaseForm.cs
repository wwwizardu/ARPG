using ARPG.Base;
using UnityEngine;

namespace ARPG.Base
{
    public class UIBaseForm : UIBase
    {
        [SerializeField] protected Transform _visual;

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

