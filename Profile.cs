using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModBusTest
{
    class Profile
    {
        public int[] Header { get; set; }
        public int[,] Steps { get; set; }

        public Profile(int[] header, int[,] steps)
        {
            Header = header;
            Steps = steps;
        }
    }
}
