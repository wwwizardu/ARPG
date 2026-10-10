using ARPG.Component;

namespace ARPG.Skill.Combat
{
    public static class CombatCredit
    {
        public static KillCreditComponent Resolve(int attackerId)
        {
            ComponentManager cm = AR.s.Component;
            int owner = attackerId;
            for (int depth = 0; depth < 16 && owner >= 0; depth++)
            {
                if (cm.TryGetComponent<CasterLinkComponent>(owner, out var link) == false) break;
                if (link.CasterEntityId == owner) return new KillCreditComponent { SourceEntityId = -1 };
                owner = link.CasterEntityId;
                if (depth == 15) return new KillCreditComponent { SourceEntityId = -1 };
            }
            bool playerParty = owner >= 0 && owner == AR.s.Data.CurrentPlayerEntityId;
            if (playerParty == false && AR.s.Npc.IsCompanionInDungeon(owner))
                playerParty = AR.s.Dungeon.IsInside && AR.s.Npc.IsNpcAlive(owner) &&
                    cm.HasComponent<NpcCompanionComponent>(owner) && cm.HasComponent<DestroyTag>(owner) == false;
            return new KillCreditComponent { SourceEntityId = owner, PlayerParty = playerParty };
        }

        public static void RecordLethalDamage(int targetId, int previousHp, int nextHp, KillCreditComponent credit)
        {
            ComponentManager cm = AR.s.Component;
            if (previousHp <= 0 || nextHp > 0 || cm.HasComponent<KillCreditComponent>(targetId)) return;
            cm.SetComponent(targetId, credit);
        }
    }
}
