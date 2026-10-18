#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;

namespace ARPG.Data
{
    /// <summary>네임드 위협 상태 (NAMED_THREAT_DESIGN §10.1). 저장되므로 새 값은 끝에만 추가한다.</summary>
    public enum NamedThreatState
    {
        None = 0,
        Active = 1,
        Slain = 2,
        // 출현 180게임시간 뒤 사라짐(또는 데이터 오류로 끝냄)
        Vanished = 3,
    }

    /// <summary>
    /// 세계 쪽 네임드 위협 기록 — WorldSaveRoot.NamedThreats (NAMED_THREAT_DESIGN §10.1). 소유자는 NamedThreatManager다.
    /// 새 저장 파트를 만들지 않고 World 파트에 싣는다. 구 세이브는 null이라 로드 때 빈 상태로 연다.
    /// </summary>
    [Serializable]
    public sealed class NamedThreatWorldState
    {
        public int NextSerial = 1;
        // 새 네임드가 나올 수 있는 가장 이른 게임시각 = 마지막 출현·처치 중 늦은 쪽 + 간격. -1 = 아직 정하지 않음
        // (새 게임·이 기능 이전 세이브) → 첫 감독 판정에서 바로 첫 네임드가 나온다 (NT3).
        [DefaultValue(-1f)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float NextAppearGameTime = -1f;
        // 영역 자리를 못 찾았을 때의 다음 시도 시각 (§4.3, 6게임시간 뒤). 출현 간격과 따로 둔다.
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float NextPlacementRetryGameTime;
        public List<NamedThreatRecord> Active = new();
        // 끝난 지 14게임일 안의 기록: 처치 반응의 관련도(피해자)·소문 문구·칭호 중복 방지에 쓴다.
        public List<NamedThreatRecord> Ended = new();
        public List<string> UsedTitles = new();

        /// <summary>null 목록·잘못된 수치를 고친다 (표·NPC를 보지 않는 보정만). 나머지는 NamedThreatManager.Initialize가 한다.</summary>
        public void Sanitize()
        {
            Active ??= new List<NamedThreatRecord>();
            Ended ??= new List<NamedThreatRecord>();
            UsedTitles ??= new List<string>();
            if (NextSerial < 1)
                NextSerial = 1;
            if (float.IsNaN(NextAppearGameTime) == true || float.IsInfinity(NextAppearGameTime) == true)
                NextAppearGameTime = -1f;
            if (float.IsNaN(NextPlacementRetryGameTime) == true || NextPlacementRetryGameTime < 0f)
                NextPlacementRetryGameTime = 0f;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                if (Active[i] == null)
                {
                    Active.RemoveAt(i);
                    continue;
                }
                Active[i].Sanitize();
                if (Active[i].ThreatId >= NextSerial)
                    NextSerial = Active[i].ThreatId + 1;
            }
            for (int i = Ended.Count - 1; i >= 0; i--)
            {
                if (Ended[i] == null)
                {
                    Ended.RemoveAt(i);
                    continue;
                }
                Ended[i].Sanitize();
                if (Ended[i].ThreatId >= NextSerial)
                    NextSerial = Ended[i].ThreatId + 1;
            }
            for (int i = UsedTitles.Count - 1; i >= 0; i--)
                if (string.IsNullOrEmpty(UsedTitles[i]) == true)
                    UsedTitles.RemoveAt(i);
        }
    }

    /// <summary>네임드 한 마리의 기록 (§10.1). 현재 위치와 체력은 저장하지 않는다 (§5.3·§5.4).</summary>
    [Serializable]
    public sealed class NamedThreatRecord
    {
        public const int MaxVictims = 16;

        public int ThreatId;
        public int TemplateId;
        public string Title = string.Empty;
        public NamedThreatState State;
        public int Stage = 1;
        // 출현 때 영역 구역 + 2 (§5.2). 실제 레벨 = 기준 레벨 + (단계 − 1)
        public int BaseLevel;
        public float AppearedGameTime;
        public float StageStartedGameTime;
        public float TerritoryX;
        public float TerritoryY;
        // 영향 마을 = 주 마을 1곳 (NT5 확정)
        public int PrimaryVillageId = -1;
        public int RumorOwnerNpcId = -1;
        // 주 마을에서 가까운 다른 마을 4곳에 1명씩 (§8.2)
        public List<int> FarRumorNpcIds = new();
        // 부상 수 (네임드 때문에 죽는 NPC는 없다, NT10)
        public int HarmCount;
        // 첫 마주침에서 켠다. 그 전에는 소문·소식창에 칭호를 쓰지 않는다 (§8.3)
        public bool TitleRevealed;
        // 그 네임드 화제가 플레이어에게 실제로 표시됐다 (§6.5 소식창 칭호·강조 줄의 기준)
        public bool PlayerHeard;
        public List<NamedThreatVictim> Victims = new();
        [DefaultValue(-1f)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float EndedGameTime = -1f;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool SlainByPlayerParty;

        [JsonIgnore]
        public UnityEngine.Vector2 Territory => new UnityEngine.Vector2(TerritoryX, TerritoryY);

        public void Sanitize()
        {
            Title ??= string.Empty;
            FarRumorNpcIds ??= new List<int>();
            Victims ??= new List<NamedThreatVictim>();
            for (int i = Victims.Count - 1; i >= 0; i--)
                if (Victims[i] == null)
                    Victims.RemoveAt(i);
            if (Victims.Count > MaxVictims)
                Victims.RemoveRange(0, Victims.Count - MaxVictims);
            if (Stage < 1) Stage = 1;
            if (Stage > 3) Stage = 3;
            if (HarmCount < 0) HarmCount = 0;
            if (float.IsNaN(AppearedGameTime) == true || AppearedGameTime < 0f) AppearedGameTime = 0f;
            if (float.IsNaN(StageStartedGameTime) == true || StageStartedGameTime < AppearedGameTime)
                StageStartedGameTime = AppearedGameTime;
        }

        public bool HasVictim(int npcId)
        {
            for (int i = 0; i < Victims.Count; i++)
                if (Victims[i].NpcId == npcId)
                    return true;
            return false;
        }
    }

    /// <summary>네임드에게 다친 NPC 한 건 (§6.3). 이름은 그때의 스냅샷이다.</summary>
    [Serializable]
    public sealed class NamedThreatVictim
    {
        public int NpcId = -1;
        public string Name = string.Empty;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Severe;
        public float When;
        public int VillageId = -1;
    }
}
