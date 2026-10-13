using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace FastGithub.UI
{
    class Program
    {
        private const string MUTEX_NAME = "Global\\FastGithub.UI";
        private const string MAIN_WINDOWS = "MainWindow.xaml";
        private const string FASTGITHUB_PATH = "fastgithub.exe";

        /// <summary>
        /// 本ui启动的后台服务进程
        /// </summary>
        private static Process? fastGithubProcess;

        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            using var mutex = new Mutex(true, MUTEX_NAME, out var isFirstInstance);
            if (isFirstInstance == false)
            {
                return;
            }

            StartFastGithub();
            SetWebBrowserDPI();
            SetWebBrowserVersion();

            var app = new Application();
            app.StartupUri = new Uri(MAIN_WINDOWS, UriKind.Relative);
            app.Run();
        }

        /// <summary>
        /// 程序集加载失败时
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private static Assembly? OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (name.EndsWith(".resources"))
            {
                return default;
            }

            var stream = Application.GetResourceStream(new Uri($"Resource/{name}.dll", UriKind.Relative)).Stream;
            var buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            return Assembly.Load(buffer);
        }

        /// <summary>
        /// 设置浏览器版本
        /// </summary>
        private static void SetWebBrowserVersion()
        {
            const string subKey = @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION";
            var registryKey = Registry.CurrentUser.OpenSubKey(subKey, true);
            if (registryKey == null)
            {
                registryKey = Registry.CurrentUser.CreateSubKey(subKey);
            }
            var name = $"{Process.GetCurrentProcess().ProcessName}.exe";
            using var webBrowser = new System.Windows.Forms.WebBrowser();
            var value = int.Parse($"{webBrowser.Version.Major}000");
            registryKey.SetValue(name, value, RegistryValueKind.DWord);
        }

        /// <summary>
        /// 设置浏览器DPI
        /// </summary>
        private static void SetWebBrowserDPI()
        {
            const string subKey = @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_96DPI_PIXEL";
            var registryKey = Registry.CurrentUser.OpenSubKey(subKey, true);
            if (registryKey == null)
            {
                registryKey = Registry.CurrentUser.CreateSubKey(subKey);
            }
            var name = $"{Process.GetCurrentProcess().ProcessName}.exe";
            registryKey.SetValue(name, 1, RegistryValueKind.DWord);
        }

        /// <summary>
        /// 启动fastgithub
        /// 已安装为windows服务时，由服务运行，不再启动本地实例
        /// </summary>
        /// <returns></returns>
        internal static void StartFastGithub()
        {
            if (File.Exists(FASTGITHUB_PATH) == false)
            {
                return;
            }

            if (ServiceUtil.IsInstalled() == true)
            {
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = FASTGITHUB_PATH,
                Arguments = $"ParentProcessId={Process.GetCurrentProcess().Id} UdpLoggerPort={UdpLogger.Port}",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            fastGithubProcess = Process.Start(startInfo);
        }

        /// <summary>
        /// 停止本ui启动的后台服务
        /// 不影响已安装的windows服务
        /// </summary>
        public static void StopFastGithub()
        {
            var hadLocalInstance = fastGithubProcess != null;
            if (hadLocalInstance == true && fastGithubProcess!.HasExited == false)
            {
                try
                {
                    fastGithubProcess.Kill();
                    fastGithubProcess.WaitForExit(5000);
                }
                catch (Exception)
                {
                }
            }

            fastGithubProcess = null;

            // 仅本地实例模式才停止dnscrypt-proxy
            // 服务模式下dnscrypt-proxy也是windows服务，不能杀
            if (hadLocalInstance == true)
            {
                StopDnscryptProxy();
            }
        }

        /// <summary>
        /// 停止dnscrypt-proxy进程
        /// </summary>
        private static void StopDnscryptProxy()
        {
            foreach (var process in Process.GetProcessesByName("dnscrypt-proxy"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
