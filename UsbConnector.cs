using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;
using System.Threading;

namespace Topuino_Client_Windows
{
    internal class UsbConnector
    {
        internal UsbConnector()
        {
            string[] portNames = SerialPort.GetPortNames();
            
            foreach (string name in portNames)
            {
                SerialPort port = new SerialPort(name, 115200, Parity.None, 8, StopBits.One);
                if (IsTopuinoPort(port))
                {
                    topuinoPort = port;
                    return;
                }
            }
            throw new Exception("找不到 Topuino 设备");
        }

        private SerialPort? topuinoPort = null;
        private ManualResetEvent portInitReceived = new ManualResetEvent(false);
        private bool portValid = false;

        internal void Send(byte[] data)
        {
            if (topuinoPort == null)
            {
                return;
            }

            byte[] buff = new byte[data.Length + 4];
            Array.Copy(data, 0, buff, 4, data.Length);
            buff[0] = 0x66;
            buff[1] = 0x77;
            buff[2] = 0xaa;
            buff[3] = 0xff;
            topuinoPort.Write(buff, 0, buff.Length);
        }

        private bool IsTopuinoPort(SerialPort port)
        {
            port.DataReceived += PortInitDataReceiver;
            port.Open();
            byte[] pingBuff = new byte[4] { 0x19, 0x26, 0x08, 0x17 };

            portValid = false;
            port.Write(pingBuff, 0, 4);
            portInitReceived.WaitOne(1000);

            if (portValid)
            {
                return true;
            }
            port.Close();
            return false;
        }

        private void PortInitDataReceiver(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort port = sender as SerialPort;

            byte[] pongBuff = new byte[2];
            int readCount = port.Read(pongBuff, 0, 2);
            if (readCount == 2)
            {
                if (pongBuff[0] == 0x68 && pongBuff[1] == 0x61)
                {
                    portValid = true;
                    portInitReceived.Set();
                }
            }
        }

        internal void Dispose()
        {
            if (topuinoPort != null)
            {
                if (topuinoPort.IsOpen)
                {
                    topuinoPort.Close();
                }
            }
            topuinoPort = null;
        }
    }
}
