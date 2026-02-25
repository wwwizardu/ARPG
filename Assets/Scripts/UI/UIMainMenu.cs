using ARPG.Base;
using UnityEngine;

namespace ARPG.UI
{
    public class UIMainMenu : UIBaseForm
    {
        public void OnClickSave()
        {
            AR.s.Data.Save();
        }
    }    
}


