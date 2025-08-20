using System;
using Newtonsoft.Json;
using UnityEngine;

namespace ARPG.Tables
{
    [Serializable]
    public class TableBase
    {
        [JsonProperty("Key")] public int Key;
    }

    [Serializable]
    public class CreatureTable : TableBase
    {
        [JsonProperty("Name")] public string Name;
        [JsonProperty("State")] public int State;
        [JsonProperty("PrefabName")] public string PrefabName;
    }
}