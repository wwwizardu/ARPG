#nullable enable

namespace ARPG.Component
{
    /// <summary>
    /// 인벤토리 UI 갱신이 필요함을 나타내는 태그 컴포넌트
    /// 인벤토리 내용이 변경되면 이 태그가 추가되고,
    /// UI 시스템에서 처리 후 제거
    /// </summary>
    public struct InventoryDirtyTag { }
}
