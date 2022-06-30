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
            }
            else
            {
                RadioButton_UsbMode.IsChecked = true;
                ComboBox_Disk0.SelectedIndex = 0;
                ComboBox_Disk1.SelectedIndex = 0;
            }

            trayIon.Icon = new Icon(@"Topuino.ico");
            trayIon.Visible = true;
            trayIon.Text = "Topuino";
            trayIon.DoubleClick += TrayIcon_DoubleClick;
        }

        private NotifyIcon trayIon = new NotifyIcon();

        private int mode = 0;
        private string sn = "";
        private List<DriveInfo> allDrives;
        private DriveInfo drive0;
        private DriveInfo drive1;

        private bool ready = false;

        private Thread? refreshThread = null;

        private ManualResetEvent requestStop = new ManualResetEvent(false);
        private ManualResetEvent stopDone = new ManualResetEvent(false);

        private OnlineConnector? onlineClient = null;
        private UsbConnector? usbClient = null;

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
            }
            catch
            {
                ShowErrorBox("初始参数加载错误，请检查配置文件");
                Close();
            }

            ApplyConfig();
            StartRun();
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

            while (!requestStop.WaitOne(0))
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
                        OfflineRun(data);
                        break;
                    default:
                        break;
                }
            }

            stopDone.Set();
        }

        private void UsbRun(MonitorData data)
        {
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

            usbClient.Send(bin);
        }

        private void OnlineRun(MonitorData data)
        {
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

            onlineClient.Post(statusInfo);
        }

        private void OfflineRun(MonitorData data)
        {

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
                    usbClient = new UsbConnector();
                }
                else if (RadioButton_OnlineMode.IsChecked == true)
                {
                    mode = 1;
                    onlineClient = new OnlineConnector();
                }
                else if (RadioButton_LocalMode.IsChecked == true)
                {
                    mode = 2;
                    // TODO
                }
                sn = TextBox_DeviceSn.Text;

                drive0 = ComboBox_Disk0.SelectedItem as DriveInfo;
                drive1 = ComboBox_Disk1.SelectedItem as DriveInfo;

                ready = true;
            }
            catch (Exception e)
            {
                ready = false;
                ShowErrorBox(e.Message);
            }
        }

        private void StartRun()
        {
            if (!ready)
            {
                return;
            }

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
                requestStop.Set();
                stopDone.WaitOne();
            }

            refreshThread = null;

            requestStop.Reset();
            stopDone.Reset();
        }

        private async void SaveConfig()
        {
            Config newConfig = new Config();
            newConfig.mode = mode;
            newConfig.sn = sn;
            newConfig.disk0 = drive0.Name;
            newConfig.disk1 = drive1.Name;
            await File.WriteAllTextAsync("Config.json", JsonConvert.SerializeObject(newConfig, Formatting.Indented));
        }

        private void ResetConnectors()
        {
            usbClient?.Dispose();
            onlineClient?.Dispose();
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

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopRun();
            ResetConnectors();
        }
    }
}
