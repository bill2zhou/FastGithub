using System;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace FastGithub.UI
{
    /// <summary>
    /// UdpLogListBox.xaml 的交互逻辑
    /// </summary>
    public partial class UdpLogListBox : UserControl
    {
        private readonly int maxLogCount = 100;
        private readonly TimeSpan maxLogAge = TimeSpan.FromMinutes(1d);
        public ObservableCollection<UdpLog> LogList { get; } = new ObservableCollection<UdpLog>();

        public UdpLogListBox()
        {
            InitializeComponent();

            this.DataContext = this;
            this.InitUdpLoggerAsync();

            // 定时清理过期日志，避免无新日志时旧日志残留
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10d)
            };
            timer.Tick += (s, e) => this.RemoveExpiredLogs();
            timer.Start();
        }

        private async void InitUdpLoggerAsync()
        {
            while (this.Dispatcher.HasShutdownStarted == false)
            {
                try
                {
                    var log = await UdpLogger.GetUdpLogAsync();
                    if (log != null)
                    {
                        // 只保留1分钟内的日志
                        if (DateTime.Now - log.Timestamp < this.maxLogAge)
                        {
                            this.LogList.Insert(0, log);
                        }

                        this.RemoveExpiredLogs();

                        if (this.LogList.Count > this.maxLogCount)
                        {
                            this.LogList.RemoveAt(this.maxLogCount);
                        }
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>
        /// 移除超过1分钟的日志
        /// </summary>
        private void RemoveExpiredLogs()
        {
            for (var i = this.LogList.Count - 1; i >= 0; i--)
            {
                if (DateTime.Now - this.LogList[i].Timestamp >= this.maxLogAge)
                {
                    this.LogList.RemoveAt(i);
                }
            }
        }

        private void MenuItem_Copy_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (this.listBox.SelectedValue is UdpLog udpLog)
            {
                udpLog.SetToClipboard();
            }
        }

        private void MenuItem_Clear_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            this.LogList.Clear();
        }
    }
}
