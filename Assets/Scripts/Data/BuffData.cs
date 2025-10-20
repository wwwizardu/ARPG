#nullable enable
using System;
using System.Collections.Generic;
using ARPG.Tables;
using UnityEngine;

namespace ARPG.Data
{
    public class BuffData
    {
        public int Id;

        public float LeftTime;

        public int Damage;

        [NonSerialized] public BuffTable? Table;
    }
}


