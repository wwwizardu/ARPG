#nullable enable
using ARPG.Manager;

namespace ARPG.Component
{
    /// <summary>한 번의 채널 시전이 소유하는 루프 이펙트와 비동기 로드 식별자.</summary>
    public struct SkillChannelEffectComponent
    {
        public EffectHandle Handle;
        public object? LoadRequest;
    }
}
