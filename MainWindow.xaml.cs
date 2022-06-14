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
                ComboBox_Disk0.SelectedIndex = 0;
                ComboBox_Disk1.SelectedIndex = 0;
            }

            trayIon.Icon = new Icon(@"Topuino.ico");
            trayIon.Visible = true;
            trayIon.Text = "Topuino";
            trayIon.DoubleClick += TrayIcon_DoubleClick;
        }

        private NotifyIcon trayIon = new NotifyIcon();

        private string sn = "";
        private List<DriveInfo> allDrives;
        private DriveInfo? drive0 = null;
        private DriveInfo? drive1 = null;

        private Config? initConfig = null;

        private Thread? refreshThread = null;

        private ManualResetEvent requestStop = new ManualResetEvent(false);
        private ManualResetEvent stopDone = new ManualResetEvent(false);

        private void LoadConfig()
        {
            try
            {
                initConfig = JsonConvert.DeserializeObject<Config>(File.ReadAllText("Config.json"));
                if (initConfig == null)
                {
                    throw new Exception();
                }

                // check if drivers missing
                foreach (DriveInfo drive in allDrives)
                {
                    if (drive.Name != initConfig.disk0 && drive.Name != initConfig.disk1)
                    {
                        initConfig.disk0 = drive.Name;
                        initConfig.disk1 = drive.Name;
                        break;
                    }
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

#pragma warning disable CS8602 // Dereference of a possibly null reference.
                Dictionary<string, string> statusInfo = new Dictionary<string, string>();
                statusInfo.Add("SN", sn);
                statusInfo.Add("CPU_PERCENT", ((int)cpuCounter.NextValue()).ToString());
                statusInfo.Add("MEM_PERCENT", ((int)ramPercentUsed).ToString());
                statusInfo.Add("DISK_PERCENT", ((int)((double)(drive0.TotalSize - drive0.AvailableFreeSpace) / drive0.TotalSize * 100)).ToString());
                statusInfo.Add("DISK1_PERCENT", ((int)((double)(drive1.TotalSize - drive1.AvailableFreeSpace) / drive1.TotalSize * 100)).ToString());
                statusInfo.Add("DISK_READ_RATE", ((int)diskReadCounter.NextValue()).ToString());
                statusInfo.Add("DISK_WRITE_RATE", ((int)diskWriteCounter.NextValue()).ToString());
                statusInfo.Add("NET_SENT_RATE", ((int)(netBytesSentAfter - netBytesSentBefore)).ToString());
                statusInfo.Add("NET_RECV_RATE", ((int)(netBytesSentAfter - netBytesSentBefore)).ToString());
#pragma warning restore CS8602 // Dereference of a possibly null reference.

                PublicComm connection = new PublicComm();
                connection.Post(statusInfo);
            }

            stopDone.Set();
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
            sn = TextBox_DeviceSn.Text;
            drive0 = ComboBox_Disk0.SelectedItem as DriveInfo;
            drive1 = ComboBox_Disk1.SelectedItem as DriveInfo;
        }

        private void StartRun()
        {
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
            if (refreshThread != null)
            {
                requestStop.Set();
                stopDone.WaitOne();
            }

            requestStop.Reset();
            stopDone.Reset();

#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (!drive0.IsReady || !drive1.IsReady)
            {
                ShowErrorBox("磁盘未就绪");
                return;
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.

            Mouse.OverrideCursor = null;

            refreshThread = new Thread(Run);
            refreshThread.Start();
        }

        private async void SaveConfig()
        {
            Config newConfig = new Config();
            newConfig.sn = sn;
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            newConfig.disk0 = drive0.Name;
            newConfig.disk1 = drive1.Name;
            await File.WriteAllTextAsync("Config.json", JsonConvert.SerializeObject(newConfig, Formatting.Indented));
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }

        private void Button_Save_Click(object sender, RoutedEventArgs e)
        {
            ApplyConfig();
            SaveConfig();
            StartRun();
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
            if (refreshThread != null)
            {
                requestStop.Set();
                stopDone.WaitOne();
            }
        }
    }
}
