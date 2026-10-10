namespace ARPG.Component
{
    /// <summary>
    /// 소문 징후 자막이 있는 입구에만 붙는다. System_DungeonEntranceSign이 판단하고, 바뀐 상태만 표시에 보낸다.
    /// </summary>
    public struct DungeonEntranceSignComponent
    {
        public bool Active;   // 마지막으로 표시에 보낸 자막 표시 상태
    }
}
