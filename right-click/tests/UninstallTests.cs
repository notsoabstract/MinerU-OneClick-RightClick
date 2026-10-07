using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Exercise the real release EXE/script with a harmless replacement for the
// low-level remover. Never call Remove-AppxPackage or certificate operations.
internal static class UninstallTests
{
    private delegate bool EnumCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr dialog, int id);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);

    private static int Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "MinerUUninstallTest-" + Guid.NewGuid().ToString("N"));
        string originalProfile = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        Environment.SetEnvironmentVariable("LOCALAPPDATA", root);
        Directory.CreateDirectory(root);
        try
        {
            File.Copy(Path.Combine(args[0], "MinerURightClick.exe"), Path.Combine(root, "MinerURightClick.exe"));
            File.Copy(Path.Combine(args[0], "uninstall.ps1"), Path.Combine(root, "uninstall.ps1"));
            // The real script resolves its remover from the isolated profile.
            string app = Path.Combine(root, "MinerURightClick", "app");
            Directory.CreateDirectory(app);
            File.WriteAllText(Path.Combine(app, "Unregister-Menu.ps1"),
                "[IO.File]::WriteAllText((Join-Path $env:LOCALAPPDATA 'removed.marker'), 'confirmed'); function global:Get-AppxPackage { }", Encoding.UTF8);
            File.WriteAllText(Path.Combine(root, "settings.fixture"), "preserve-test-settings");
            Check(root, false, 7, "EXE: No cancels without removal or success dialog");
            Check(root, false, 0, "EXE: closing confirmation cancels without removal");
            Check(root, true, 7, "Script: No exits as cancellation without removal");
            Check(root, true, 6, "Script: Yes reaches remover");
            Check(root, false, 6, "EXE: Yes reaches remover and success dialog");
            Console.WriteLine("PASS: five isolated uninstall scenarios; no real menu or certificate changes.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Environment.SetEnvironmentVariable("LOCALAPPDATA", originalProfile); Directory.Delete(root, true); }
    }

    private static void Check(string root, bool direct, int button, string label)
    {
        string marker = Path.Combine(root, "removed.marker");
        File.Delete(marker);
        ProcessStartInfo start = direct
            ? new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "uninstall.ps1") + "\"")
            : new ProcessStartInfo(Path.Combine(root, "MinerURightClick.exe"), "--uninstall");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        using (Process process = Process.Start(start))
        {
            int dialogProcess = process.Id;
            try
            {
                if (!direct)
                {
                    Stopwatch children = Stopwatch.StartNew();
                    bool found = false;
                    while (children.ElapsedMilliseconds < 15000 && !process.HasExited && !found)
                    {
                        using (ManagementObjectSearcher query = new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE ParentProcessId=" + process.Id))
                        using (ManagementObjectCollection results = query.Get())
                            foreach (ManagementObject child in results) { dialogProcess = Convert.ToInt32(child["ProcessId"]); found = true; break; }
                        if (!found) Thread.Sleep(100);
                    }
                    Assert(found, "Application did not start uninstall script");
                }
                IntPtr dialog = WaitDialog(dialogProcess, "移除 MinerU 右键菜单", 7);
                int defaultButton = SendMessage(dialog, 0x0400, IntPtr.Zero, IntPtr.Zero).ToInt32() & 0xffff;
                Assert(defaultButton == 7, "Default confirmation button must be No");
                Assert(!File.Exists(marker), "Removal started before confirmation");
                if (button == 0) SendMessage(dialog, 0x0010, IntPtr.Zero, IntPtr.Zero);
                else SendMessage(dialog, 0x0111, new IntPtr(button), GetDlgItem(dialog, button));
                if (!direct && button == 6)
                {
                    IntPtr success = WaitDialog(process.Id, "MinerU 右键识别", 0);
                    Assert(File.Exists(marker), "Success dialog appeared before the fake remover ran");
                    // The themed completion dialog can nest its OK button.
                    SendMessage(success, 0x0010, IntPtr.Zero, IntPtr.Zero);
                }
                Assert(process.WaitForExit(10000), "Unexpected dialog or unfinished uninstall");
                Assert(process.ExitCode == (direct && button != 6 ? 2 : 0), "Unexpected exit code " + process.ExitCode);
                Assert(File.Exists(marker) == (button == 6), "Removal decision mismatch");
                Assert(File.ReadAllText(Path.Combine(root, "settings.fixture")) == "preserve-test-settings", "Settings changed");
                Console.WriteLine("PASS " + label);
            }
            finally
            {
                if (!process.HasExited)
                {
                    if (dialogProcess != process.Id)
                    {
                        try { using (Process child = Process.GetProcessById(dialogProcess)) { child.Kill(); child.WaitForExit(5000); } } catch (ArgumentException) { }
                    }
                    process.Kill(); process.WaitForExit(5000);
                }
            }
        }
    }

    private static IntPtr WaitDialog(int process, string title, int button)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 15000)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                uint owner; GetWindowThreadProcessId(window, out owner);
                if (owner != process) return true;
                StringBuilder text = new StringBuilder(256); GetWindowText(window, text, text.Capacity);
                if (text.ToString() == title && IsWindowVisible(window) && (button == 0 || GetDlgItem(window, button) != IntPtr.Zero)) { result = window; return false; }
                return true;
            }, IntPtr.Zero);
            if (result != IntPtr.Zero) return result;
            Thread.Sleep(100);
        }
        throw new Exception("Dialog not found: " + title);
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
}
