using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;

namespace FastGithub.UI
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly System.Windows.Forms.NotifyIcon notifyIcon;
        private const string FASTGITHUB_UI = "FastGithub.UI";
        private const string RELEASES_URI = "https://github.com/creazyboyone/FastGithub";

        public MainWindow()
        {
            InitializeComponent();

            var version = this.GetType().Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            this.Title = $"{FASTGITHUB_UI} v{version}";
            this.notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Visible = true,
                Text = FASTGITHUB_UI,
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath)
            };

            this.UpdateTrayMenu();

            this.notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    this.Show();
                    this.Activate();
                    this.WindowState = WindowState.Normal;
                }
            };
        }

        /// <summary>
        /// 刷新托盘右键菜单
        /// </summary>
        private void UpdateTrayMenu()
        {
            var upgrade = new System.Windows.Forms.MenuItem("检测更新(&U)");
            upgrade.Click += (s, e) => Process.Start(RELEASES_URI);

            var items = new System.Collections.Generic.List<System.Windows.Forms.MenuItem> { upgrade };

            // 根据服务安装状态显示安装或卸载
            if (ServiceUtil.IsInstalled() == true)
            {
                var uninstall = new System.Windows.Forms.MenuItem("卸载服务(&U)");
                uninstall.Click += OnUninstallServiceClick;
                items.Add(uninstall);
            }
            else
            {
                var install = new System.Windows.Forms.MenuItem("安装到服务(&I)");
                install.Click += OnInstallServiceClick;
                items.Add(install);
            }

            var exit = new System.Windows.Forms.MenuItem("关闭应用(&C)");
            exit.Click += (s, e) => this.Close();
            items.Add(exit);

            this.notifyIcon.ContextMenu = new System.Windows.Forms.ContextMenu(items.ToArray());
        }

        /// <summary>
        /// 安装到服务
        /// </summary>
        private void OnInstallServiceClick(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "将把 FastGithub 安装为 windows 服务并开机自启动，安装后本窗口关闭不影响服务运行。是否继续？",
                FASTGITHUB_UI, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // 先停止本ui启动的实例，避免与服务争抢端口
            Program.StopFastGithub();

            if (ServiceUtil.Install() == true)
            {
                MessageBox.Show("服务安装成功，FastGithub 已后台运行", FASTGITHUB_UI, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("服务安装失败，请查看日志", FASTGITHUB_UI, MessageBoxButton.OK, MessageBoxImage.Warning);
                // 安装失败则恢复本地实例
                Program.StartFastGithub();
            }

            this.UpdateTrayMenu();
        }

        /// <summary>
        /// 卸载服务
        /// </summary>
        private void OnUninstallServiceClick(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "将停止并卸载 FastGithub 服务，卸载后仅在本窗口打开时运行。是否继续？",
                FASTGITHUB_UI, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes && result != MessageBoxResult.OK)
            {
                return;
            }

            if (ServiceUtil.Uninstall() == true)
            {
                // 卸载后恢复本地实例，保持当前可用
                Program.StartFastGithub();
                MessageBox.Show("服务已卸载，已恢复本机运行", FASTGITHUB_UI, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("服务卸载失败，请查看日志", FASTGITHUB_UI, MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            this.UpdateTrayMenu();
        }


        /// <summary>
        /// 拦截最小化事件
        /// </summary>
        /// <param name="e"></param>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwndSource = (HwndSource)PresentationSource.FromVisual(this);
            hwndSource.AddHook(WndProc);

            IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                const int WM_SYSCOMMAND = 0x112;
                const int SC_MINIMIZE = 0xf020;
                const int SC_CLOSE = 0xf060;

                if (msg == WM_SYSCOMMAND)
                {
                    if (wParam.ToInt32() == SC_MINIMIZE || wParam.ToInt32() == SC_CLOSE)
                    {
                        this.Hide();
                        handled = true;
                    }
                }
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// 关闭时
        /// </summary>
        /// <param name="e"></param>
        protected override void OnClosed(EventArgs e)
        {
            // ui关闭时，停止本ui启动的后台服务（不影响已安装的windows服务）
            Program.StopFastGithub();

            this.notifyIcon.Icon = null;
            this.notifyIcon.Dispose();
            base.OnClosed(e);
        }
    }
}
