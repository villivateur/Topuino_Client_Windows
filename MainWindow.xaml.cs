using System;
using System.Windows;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.Windows.Input;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace Topuino_Client_Windows
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            trayIon.Icon = new Icon(@"Topuino.ico");
            trayIon.Visible = true;
            trayIon.Text = "Topuino";
            trayIon.DoubleClick += TrayIcon_DoubleClick;

            allDrives = new List<DriveInfo>();
            DriveInfo[] drives = DriveInfo.GetDrives();
            foreach (DriveInfo drive in drives)
            {
                allDrives.Add(drive);
            }
            if (allDrives.Count == 0)
            {
                ShowErrorBox("找不到可以监控的磁盘");
                Close();
            }

            ComboBox_Disk0.ItemsSource = allDrives;
            ComboBox_Disk1.ItemsSource = allDrives;

            if (File.Exists("Config.json"))
            {
                ShowInTaskbar = true;
                Visibility = Visibility.Hidden;
                LoadConfig();
                ApplyConfig();
                StartRun();
            }
            else
            {
                RadioButton_UsbMode.IsChecked = true;
                ComboBox_Disk0.SelectedIndex = 0;
                ComboBox_Disk1.SelectedIndex = 0;
            }
        }

        private NotifyIcon trayIon = new NotifyIcon();

        private int mode = 0;
        private string sn = "";
        private string ipAddr = "";
        private List<DriveInfo> allDrives;
        private DriveInfo drive0;
        private DriveInfo drive1;

        private Thread? refreshThread = null;

        private ManualResetEvent requestStopEvent = new ManualResetEvent(false);
        private ManualResetEvent stopDoneEvent = new ManualResetEvent(false);

        private OnlineConnector? onlineConnector = null;
        private UsbConnector? usbConnector = null;
        private LocalConnector? localConnector = null;

        private void LoadConfig()
        {
            try
            {
                Config? initConfig = JsonConvert.DeserializeObject<Config>(File.ReadAllText("Config.json"));
                if (initConfig == null)
                {
                    throw new Exception();
                }

                switch (initConfig.mode)
                {
                    case 0:
                        RadioButton_UsbMode.IsChecked = true;
                        break;
                    case 1:
                        RadioButton_OnlineMode.IsChecked = true;
                        break;
                    case 2:
                        RadioButton_LocalMode.IsChecked = true;
                        break;
                    default:
                        RadioButton_UsbMode.IsChecked = true;
                        break;
                }

                // check if drivers missing
                bool disk0Found = false;
                bool disk1Fount = false;
                foreach (DriveInfo drive in allDrives)
                {
                    if (drive.Name == initConfig.disk0)
                    {
                        disk0Found = true;
                    }
                    if (drive.Name == initConfig.disk1)
                    {
                        disk1Fount = true;
                    }
                }
                if (!disk0Found)
                {
                    ShowErrorBox("找不到磁盘0，已切换为默认磁盘");
                    initConfig.disk0 = allDrives[0].Name;
                }
                if (!disk1Fount)
                {
                    ShowErrorBox("找不到磁盘1，已切换为默认磁盘");
                    initConfig.disk1 = allDrives[0].Name;
                }

                for (int i = 0; i < allDrives.Count; i++)
                {
                    if (allDrives[i].Name == initConfig.disk0)
                    {
                        ComboBox_Disk0.SelectedIndex = i;
                    }
                    if (allDrives[i].Name == initConfig.disk1)
                    {
                        ComboBox_Disk1.SelectedIndex = i;
                    }
                }

                TextBox_DeviceSn.Text = initConfig.sn;
                TextBox_DeviceIp.Text = initConfig.ip;
            }
            catch
            {
                ShowErrorBox("初始参数加载错误，请检查配置文件");
                Close();
            }
        }

        private void Run()
        {
            PerformanceCounter cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            long ramAvailable = PerformanceInfo.GetPhysicalAvailableMemoryInMiB();
            long ramTotal = PerformanceInfo.GetTotalMemoryInMiB();
            long ramPercentFree = ramAvailable * 100 / ramTotal;
            long ramPercentUsed = 100 - ramPercentFree;
            PerformanceCounter diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            PerformanceCounter diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();
            long netBytesSentBefore;
            long netBytesSentAfter;
            long netBytesReceiveBefore;
            long netBytesReceiveAfter;

            while (!requestStopEvent.WaitOne(0))
            {
                netBytesSentBefore = 0;
                netBytesSentAfter = 0;
                netBytesReceiveBefore = 0;
                netBytesReceiveAfter = 0;
                foreach (NetworkInterface ni in interfaces)
                {
                    netBytesSentBefore += ni.GetIPv4Statistics().BytesSent;
                    netBytesReceiveBefore += ni.GetIPv4Statistics().BytesReceived;
                }

                Thread.Sleep(1000);

                foreach (NetworkInterface ni in interfaces)
                {
                    netBytesSentAfter += ni.GetIPStatistics().BytesSent;
                    netBytesReceiveAfter += ni.GetIPStatistics().BytesReceived;
                }

                MonitorData data = new MonitorData
                {
                    cpuPercent = (byte)cpuCounter.NextValue(),
                    memPercent = (byte)ramPercentUsed,
                    disk0Percent = (byte)((double)(drive0.TotalSize - drive0.AvailableFreeSpace) / drive0.TotalSize * 100),
                    disk1Percent = (byte)((double)(drive1.TotalSize - drive1.AvailableFreeSpace) / drive1.TotalSize * 100),
                    diskReadRate = (uint)diskReadCounter.NextValue(),
                    diskWriteRate = (uint)diskWriteCounter.NextValue(),
                    netSentRate = (uint)(netBytesSentAfter - netBytesSentBefore),
                    netRecvRate = (uint)(netBytesReceiveAfter - netBytesReceiveBefore),
                };

                switch (mode)
                {
                    case 0:
                        UsbRun(data);
                        break;
                    case 1:
                        OnlineRun(data);
                        break;
                    case 2:
                        LocalRun(data);
                        break;
                    default:
                        break;
                }
            }

            stopDoneEvent.Set();
        }

        private void UsbRun(MonitorData data)
        {
            if (usbConnector == null)
            {
                try
                {
                    usbConnector = new UsbConnector();
                    ShowConnected();
                }
                catch
                {
                    usbConnector = null;
                    ShowDisconnected();
                    return;
                }
            }

            int size = Marshal.SizeOf(data);
            byte[] bin = new byte[size];
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(data, ptr, true);
                Marshal.Copy(ptr, bin, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            try
            {
                usbConnector.Send(bin);
            }
            catch
            {
                usbConnector.Dispose();
                usbConnector = null;
                ShowDisconnected();
            }
        }

        private void OnlineRun(MonitorData data)
        {
            if (onlineConnector == null)
            {
                onlineConnector = new OnlineConnector();
            }

            Dictionary<string, string> statusInfo = new Dictionary<string, string>();
            statusInfo.Add("SN", sn);
            statusInfo.Add("CPU_PERCENT", data.cpuPercent.ToString());
            statusInfo.Add("MEM_PERCENT", data.memPercent.ToString());
            statusInfo.Add("DISK_PERCENT", data.disk0Percent.ToString());
            statusInfo.Add("DISK1_PERCENT", data.disk1Percent.ToString());
            statusInfo.Add("DISK_READ_RATE", data.diskReadRate.ToString());
            statusInfo.Add("DISK_WRITE_RATE", data.diskWriteRate.ToString());
            statusInfo.Add("NET_SENT_RATE", data.netSentRate.ToString());
            statusInfo.Add("NET_RECV_RATE", data.netRecvRate.ToString());

            try
            {
                onlineConnector.Post(statusInfo).Wait();
                ShowConnected();
            }
            catch
            {
                onlineConnector.Dispose();
                onlineConnector = null;
                ShowDisconnected();
            }
        }

        private void LocalRun(MonitorData data)
        {
            if (localConnector == null)
            {
                try
                {
                    localConnector = new LocalConnector(ipAddr);
                    ShowConnected();
                }
                catch
                {
                    localConnector = null;
                    ShowDisconnected();
                    return;
                }
            }
            int size = Marshal.SizeOf(data);
            byte[] bin = new byte[size];
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(data, ptr, true);
                Marshal.Copy(ptr, bin, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            try
            {
                localConnector.Send(bin);
            }
            catch
            {
                localConnector.Dispose();
                localConnector = null;
                ShowDisconnected();
            }
        }


        public void ShowErrorBox(string msg)
        {
            System.Windows.MessageBox.Show(
                System.Windows.Application.Current.MainWindow,
                msg,
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        private void ApplyConfig()
        {
            try
            {
                if (RadioButton_UsbMode.IsChecked == true)
                {
                    mode = 0;
                }
                else if (RadioButton_OnlineMode.IsChecked == true)
                {
                    mode = 1;
                }
                else if (RadioButton_LocalMode.IsChecked == true)
                {
                    mode = 2;
                }

                drive0 = ComboBox_Disk0.SelectedItem as DriveInfo;
                drive1 = ComboBox_Disk1.SelectedItem as DriveInfo;

                sn = TextBox_DeviceSn.Text;

                ipAddr = TextBox_DeviceIp.Text;
            }
            catch (Exception e)
            {
                ShowErrorBox(e.Message);
            }
        }

        private void StartRun()
        {
            if (!drive0.IsReady || !drive1.IsReady)
            {
                ShowErrorBox("磁盘未就绪");
                return;
            }

            refreshThread = new Thread(Run);
            refreshThread.Start();
        }

        private void StopRun()
        {
            if (refreshThread != null)
            {
                requestStopEvent.Set();
                stopDoneEvent.WaitOne();
            }

            refreshThread = null;

            requestStopEvent.Reset();
            stopDoneEvent.Reset();
        }

        private async void SaveConfig()
        {
            Config newConfig = new Config();
            newConfig.mode = mode;
            newConfig.disk0 = drive0.Name;
            newConfig.disk1 = drive1.Name;
            newConfig.sn = sn;
            newConfig.ip = ipAddr;
            await File.WriteAllTextAsync("Config.json", JsonConvert.SerializeObject(newConfig, Formatting.Indented));
        }

        private void ResetConnectors()
        {
            usbConnector?.Dispose();
            onlineConnector?.Dispose();
            localConnector?.Dispose();
        }

        private void Button_Save_Click(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            StopRun();
            ResetConnectors();
            ApplyConfig();
            SaveConfig();
            StartRun();
            Mouse.OverrideCursor = null;
        }

        private void TrayIcon_DoubleClick(object? sender, EventArgs e)
        {
            Visibility = Visibility.Visible;
        }

        private void Button_Hide_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Hidden;
        }

        private void ShowConnected()
        {
            if (requestStopEvent.WaitOne(0))
            {
                return;
            }

            Dispatcher.Invoke(() => {
                TextBlock_Status.Text = "已连接";
                TextBlock_Status.Foreground = System.Windows.Media.Brushes.Green;
            });
        }

        private void ShowDisconnected()
        {
            if (requestStopEvent.WaitOne(0))
            {
                return;
            }

            Dispatcher.Invoke(() => {
                TextBlock_Status.Text = "未连接";
                TextBlock_Status.Foreground = System.Windows.Media.Brushes.Red;
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopRun();
            ResetConnectors();
        }
    }
}
