#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace ARPG.Manager
{
    public class PlayerManager
    {
        private List<Creature.ArpgPlayer> _players = new List<Creature.ArpgPlayer>();
        private Creature.ArpgPlayer? _myPlayer = null;

        public Creature.ArpgPlayer? MyPlayers => _myPlayer;

        public void AddPlayer(Creature.ArpgPlayer player)
        {
            if (player == null)
                return;

            if (_players.Contains(player) == false)
            {
                _players.Add(player);
            }

            _myPlayer = player;
        }
        
        public List<Creature.ArpgPlayer> GetAllPlayers()
        {
            return _players;
        }
    }
}
