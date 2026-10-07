using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MinerURightClick
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length >= 2 && args[0] == "--import-token")
                {
                    SettingsStore.ImportToken(args[1]);
                    return 0;
                }
                if (args.Length == 1 && (args[0] == "--install" || args[0] == "--uninstall"))
                {
                    RunRegistration(args[0] == "--install");
                    return 0;
                }
                if (args.Length == 0 || (args.Length == 1 && args[0] == "--settings"))
                {
                    ToolSettings settings;
                    try { settings = SettingsStore.Load(); }
                    catch { settings = new ToolSettings(); }
                    Application.Run(new SettingsForm(settings, SettingsStore.Save));
                    return 0;
                }
                if (args.Length != 1) throw new InvalidOperationException("请右键单个文件，选择“用 MinerU 识别”。");
                string filePath = Path.GetFullPath(args[0]);
                if (!File.Exists(filePath)) throw new FileNotFoundException("文件不存在或无法访问。", filePath);
                if (!RecognitionRunner.IsSupportedPath(filePath)) throw new InvalidOperationException("支持 PDF、Word、PPT、Excel 和常见图片文件。");
                string jobKey = JobKey(filePath);
                bool created;
                using (Mutex mutex = new Mutex(true, "Local\\MinerURightClick-" + jobKey, out created))
                {
                    if (!created)
                    {
                        FocusExistingJob(jobKey);
                        return 0;
                    }
                    try
                    {
                        using (WorkForm form = new WorkForm(filePath, jobKey)) Application.Run(form);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "MinerU 右键识别", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
        }

        internal static string JobKey(string filePath)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(filePath).ToUpperInvariant()))).Replace("-", "");
        }

        internal static string QuoteArgument(string value)
        {
            StringBuilder result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in value)
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') { result.Append('\\', slashes * 2 + 1); result.Append('"'); }
                else { result.Append('\\', slashes); result.Append(character); }
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            result.Append('"');
            return result.ToString();
        }

        private static void RunRegistration(bool install)
        {
            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, install ? "install.ps1" : "uninstall.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException("没有找到安装文件，请从完整程序目录启动。", script);
            ProcessStartInfo start = new ProcessStartInfo("powershell.exe", "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File " + QuoteArgument(script));
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            string output;
            string error;
            int exitCode;
            using (Process process = Process.Start(start))
            {
                var readOutput = process.StandardOutput.ReadToEndAsync();
                var readError = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                output = readOutput.Result;
                error = readError.Result;
                exitCode = process.ExitCode;
            }
            // A declined uninstall is a normal cancellation, with no success dialog.
            if (!install && exitCode == 2) return;
            if (exitCode != 0) throw new InvalidOperationException("右键菜单操作未完成：\n" + (String.IsNullOrWhiteSpace(error) ? output : error));
            MessageBox.Show(install ? "已添加“用 MinerU 识别”。\n请重新打开文件所在目录查看右键菜单。" : "已移除 MinerU 识别右键菜单。", "MinerU 右键识别", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void FocusExistingJob(string key)
        {
            string property = "MinerURightClick.Job." + key;
            EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                if (GetProp(window, property) == IntPtr.Zero) return true;
                ShowWindowAsync(window, 9);
                SetForegroundWindow(window);
                return false;
            }, IntPtr.Zero);
        }

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    }
}
