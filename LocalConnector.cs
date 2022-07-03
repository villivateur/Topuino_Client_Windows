using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;


namespace Topuino_Client_Windows
{
    internal class LocalConnector
    {
        internal LocalConnector(string addr)
        {
            client = new UdpClient();
            client.Connect(addr, 32737);
        }

        private UdpClient client;

        internal void Send(byte[] data)
        {
            byte[] buff = new byte[data.Length + 4];
            Array.Copy(data, 0, buff, 4, data.Length);
            buff[0] = 0x19;
            buff[1] = 0x26;
            buff[2] = 0x08;
            buff[3] = 0x17;

            client.Send(buff, buff.Length);
        }

        internal void Dispose()
        {
            client.Dispose();
        }
    }
}
