using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dino.Services
{
    public class GameState
    {
        public bool Running { get; set; }

        public bool Paused { get; set; }

        public bool Crashed { get; set; }

        public double Speed { get; set; }

        public int Score { get; set; }

        public int TimeSeconds { get; set; }
    }
}