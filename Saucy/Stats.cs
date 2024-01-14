﻿using FFTriadBuddy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saucy
{
    public class Stats
    {
        public int GamesPlayedWithSaucy = 0;

        public int GamesWonWithSaucy = 0;

        public int GamesLostWithSaucy = 0;

        public int GamesDrawnWithSaucy = 0;

        public int CardsDroppedWithSaucy = 0;

        public int MGPWon = 0;

        public Dictionary<string, int> NPCsPlayed = new Dictionary<string, int>();

        public Dictionary<uint, int> CardsWon = new Dictionary<uint, int>();

        public int CuffMGP = 0;

        public int CuffBrutals = 0;

        public int CuffPunishings = 0;

        public int CuffBruisings = 0;

        public int CuffGamesPlayed = 0;

        public int LimbGamesPlayed = 0;

        public int LimbGamesWon = 0;

        public int LimbMGP = 0;

        public uint LimbTime = 0;

        public uint LimbLevel0 = 0;
        public uint LimbLevel1 = 0;
        public uint LimbLevel2 = 0;
        public uint LimbLevel3 = 0;
        public uint LimbLevel4 = 0;
        public uint LimbLevel5 = 0;
    }
}
