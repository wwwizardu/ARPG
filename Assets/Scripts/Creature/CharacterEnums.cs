namespace ARPG.Creature
{
    public enum CharacterConditions // *** 상태를 추가할 때 위치에 신경써서 추가해주세요 ***
    {
        None,
        Normal,
        BlockMoveAnimation, // 이 밑으로는 캐릭터 MoveState에 따라 애니메이션을 변경해주지 않는 상태
        UseSkill,
        InstallStructure,
        Interact,
        Stunned,            // Stunned 밑으로는 Input도 영양을 주지 못하는 상태
        Dead,
        Revival,
    }

    public enum MovementStates
    {
        None,
        Idle,
        Walking,
        Jumping,
    }
}
