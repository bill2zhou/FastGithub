using System;
using System.Diagnostics;
using System.IO;

namespace FastGithub.UI
{
    /// <summary>
    /// windows服务工具
    /// 通过fastgithub.exe的start/stop指令安装/卸载服务
    /// </summary>
    static class ServiceUtil
    {
        private const string SERVICE_NAME = "fastgithub";
        private static readonly string fastGithubPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fastgithub.exe");

        /// <summary>
        /// 服务是否已安装
        /// </summary>
        /// <returns></returns>
        public static bool IsInstalled()
        {
            return RunProcess("sc", $"query {SERVICE_NAME}", out _) == 0;
        }

        /// <summary>
        /// 服务是否正在运行
        /// </summary>
        /// <returns></returns>
        public static bool IsRunning()
        {
            if (RunProcess("sc", $"query {SERVICE_NAME}", out var output) != 0)
            {
                return false;
            }
            return output != null && output.Contains("RUNNING");
        }

        /// <summary>
        /// 安装并启动服务
        /// </summary>
        /// <returns></returns>
        public static bool Install()
        {
            if (File.Exists(fastGithubPath) == false)
            {
                return false;
            }

            RunProcess(fastGithubPath, "start", out _);
            return IsInstalled();
        }

        /// <summary>
        /// 停止并卸载服务
        /// </summary>
        /// <returns></returns>
        public static bool Uninstall()
        {
            if (File.Exists(fastGithubPath) == false)
            {
                return false;
            }

            RunProcess(fastGithubPath, "stop", out _);
            return IsInstalled() == false;
        }

        /// <summary>
        /// 执行进程并等待退出
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="arguments"></param>
        /// <param name="output"></param>
        /// <returns>退出码</returns>
        private static int RunProcess(string fileName, string arguments, out string? output)
        {
            output = null;
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return -1;
                }
                output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
