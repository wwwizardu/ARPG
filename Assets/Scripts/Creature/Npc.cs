using UnityEngine;

namespace ARPG.Creature
{
    public class Npc : CharacterBase
    {
        // protected Tables.NpcTable? _npcTable = null;

        // public new Tables.NpcTable? Table { get { return _npcTable; } }

        public override void Initialize()
        {
            base.Initialize();

            //_team = GlobalEnum.TeamType.Npc;
        }

        public override void Reset()
        {
            base.Reset();
        }

        public override bool LoadTable(int inId)
        {
            // _npcTable = AR.s.Data.GetNpc(inId);
            // if (_npcTable == null)
            // {
            //     Debug.LogError($"[Npc] LoadTable - NpcTable not found for Id: {inId}");
            //     return false;
            // }

            return true;
        }
    }
}
