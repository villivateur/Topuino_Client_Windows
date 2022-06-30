using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Topuino_Client_Windows
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct MonitorData
    {
        public byte cpuPercent;
        public byte memPercent;
        public byte disk0Percent;
        public byte disk1Percent;
        public uint diskReadRate;
        public uint diskWriteRate;
        public uint netSentRate;
        public uint netRecvRate;
    }
}
