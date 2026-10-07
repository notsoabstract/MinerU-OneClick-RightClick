using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MinerURightClick
{
    internal sealed class SettingsForm : Form
    {
        internal readonly TextBox TokenInput = new TextBox();
        internal readonly CheckBox MarkdownSwitch = new CheckBox();
        internal readonly CheckBox WordSwitch = new CheckBox();
        private readonly Action<ToolSettings> save;

        internal SettingsForm(ToolSettings settings, Action<ToolSettings> saveSettings)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            save = saveSettings;
            Text = "MinerU 右键识别 · 设置";
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(480, 330);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;
            Label title = new Label { Text = "识别设置", Font = new Font(Font, FontStyle.Bold), AutoSize = true, Location = new Point(24, 20) };
            Label tokenLabel = new Label { Text = "MinerU Token", AutoSize = true, Location = new Point(24, 60) };
            TokenInput.Location = new Point(24, 87);
            TokenInput.Size = new Size(430, 28);
            TokenInput.UseSystemPasswordChar = true;
            TokenInput.Text = settings.Token ?? "";
            Label formats = new Label { Text = "导出格式", AutoSize = true, Location = new Point(24, 135) };
            MarkdownSwitch.Text = "Markdown（.md）";
            MarkdownSwitch.AutoSize = true;
            MarkdownSwitch.Location = new Point(24, 165);
            MarkdownSwitch.Checked = settings.ExportMarkdown;
            WordSwitch.Text = "Word（.docx）";
            WordSwitch.AutoSize = true;
            WordSwitch.Location = new Point(255, 165);
            WordSwitch.Checked = settings.ExportWord;
            Label note = new Label { Font = Font, Text = "可同时开启。结果保存在原文件旁，重名自动加编号。", AutoSize = false, Location = new Point(24, 205), Size = new Size(430, 45), ForeColor = SystemColors.GrayText };
            Button saveButton = new Button { Text = "保存", Size = new Size(92, 35), Location = new Point(362, 273) };
            saveButton.Click += delegate { SaveSelection(); };
            Controls.AddRange(new Control[] { title, tokenLabel, TokenInput, formats, MarkdownSwitch, WordSwitch, note, saveButton });
            AcceptButton = saveButton;
            ResumeLayout(false);
            PerformLayout();
        }

        internal ToolSettings ReadSelection()
        {
            ToolSettings selected = new ToolSettings { Token = SettingsStore.NormalizeToken(TokenInput.Text), ExportMarkdown = MarkdownSwitch.Checked, ExportWord = WordSwitch.Checked };
            if (String.IsNullOrWhiteSpace(selected.Token)) throw new InvalidOperationException("请填写 MinerU Token。");
            if (!selected.ExportMarkdown && !selected.ExportWord) throw new InvalidOperationException("请至少选择一种导出格式。");
            return selected;
        }

        private void SaveSelection()
        {
            try
            {
                save(ReadSelection());
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "设置", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
    }

    internal sealed class WorkForm : Form
    {
        private readonly string source;
        private readonly string key;
        internal readonly Label StatusLabel = new Label();
        private readonly Label details = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button primary = new Button();
        private readonly Button settingsButton = new Button();
        private readonly Button againButton = new Button();
        private readonly NotifyIcon notification = new NotifyIcon();
        private CancellationTokenSource cancellation;
        private List<string> outputs;
        private bool busy;
        private string alternateOutput;

        internal WorkForm(string sourcePath, string jobKey)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            source = sourcePath;
            key = jobKey;
            Text = "MinerU 识别 · " + Path.GetFileName(source);
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(480, 255);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;
            Label file = new Label { Text = Path.GetFileName(source), AutoEllipsis = true, Location = new Point(24, 22), Size = new Size(430, 30), Font = new Font(Font, FontStyle.Bold) };
            StatusLabel.Font = Font;
            StatusLabel.Location = new Point(24, 65);
            StatusLabel.Size = new Size(430, 28);
            StatusLabel.Text = "准备识别";
            progress.Location = new Point(24, 104);
            progress.Size = new Size(430, 12);
            progress.Style = ProgressBarStyle.Marquee;
            details.Font = Font;
            details.Location = new Point(24, 136);
            details.Size = new Size(430, 45);
            details.ForeColor = SystemColors.GrayText;
            details.Text = "可以最小化，完成后在这里打开结果目录。";
            primary.Text = "重试";
            primary.Location = new Point(342, 199);
            primary.Size = new Size(112, 34);
            primary.Visible = false;
            primary.Click += async delegate
            {
                if (outputs != null) { OpenOutputDirectory(); return; }
                await StartRecognition();
            };
            settingsButton.Text = "设置";
            settingsButton.Location = new Point(24, 199);
            settingsButton.Size = new Size(85, 34);
            settingsButton.Click += delegate
            {
                using (SettingsForm settings = new SettingsForm(SettingsStore.Load(), SettingsStore.Save)) settings.ShowDialog(this);
            };
            againButton.Text = "重新识别";
            againButton.Location = new Point(123, 199);
            againButton.Size = new Size(100, 34);
            againButton.Visible = false;
            againButton.Click += async delegate { outputs = null; await StartRecognition(); };
            notification.Icon = SystemIcons.Application;
            notification.Text = "MinerU 右键识别";
            notification.DoubleClick += delegate { WindowState = FormWindowState.Normal; Show(); Activate(); };
            Controls.AddRange(new Control[] { file, StatusLabel, progress, details, primary, settingsButton, againButton });
            Shown += async delegate { await StartRecognition(); };
            FormClosing += delegate { if (cancellation != null) cancellation.Cancel(); };
            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetProp(Handle, "MinerURightClick.Job." + key, new IntPtr(1));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { notification.Dispose(); if (cancellation != null) cancellation.Dispose(); }
            base.Dispose(disposing);
        }

        private async Task StartRecognition()
        {
            if (busy) return;
            ToolSettings settings;
            try { settings = SettingsStore.Load(); }
            catch { settings = new ToolSettings(); }
            if (String.IsNullOrWhiteSpace(settings.Token) || (!settings.ExportMarkdown && !settings.ExportWord))
            {
                using (SettingsForm editor = new SettingsForm(settings, SettingsStore.Save))
                {
                    if (editor.ShowDialog(this) != DialogResult.OK) { StatusLabel.Text = "等待设置 Token"; primary.Visible = true; return; }
                }
                settings = SettingsStore.Load();
            }
            busy = true;
            primary.Visible = false;
            againButton.Visible = false;
            settingsButton.Enabled = false;
            progress.Visible = true;
            details.Text = "可以最小化；关闭后再次右键可继续查询上次任务。";
            if (cancellation != null) cancellation.Dispose();
            cancellation = new CancellationTokenSource();
            try
            {
                using (RecognitionRunner runner = new RecognitionRunner())
                    outputs = await runner.ProcessAsync(source, settings, UpdateStatus, cancellation.Token, alternateOutput);
                if (IsDisposed) return;
                StatusLabel.Text = "识别完成";
                details.Text = "已保存：" + String.Join("、", outputs.ConvertAll(Path.GetFileName));
                primary.Text = "打开结果目录";
                primary.Visible = true;
                againButton.Visible = true;
                progress.Visible = false;
                notification.Visible = true;
                notification.ShowBalloonTip(5000, "MinerU 识别完成", Path.GetFileName(source) + " 的结果已保存。", ToolTipIcon.Info);
            }
            catch (OperationCanceledException) { }
            catch (ExportPermissionException)
            {
                if (IsDisposed) return;
                StatusLabel.Text = "原文件夹无法写入";
                details.Text = "识别结果已暂存，请选择其他保存位置。";
                using (FolderBrowserDialog folder = new FolderBrowserDialog { Description = "选择识别结果保存位置" })
                {
                    if (folder.ShowDialog(this) == DialogResult.OK) alternateOutput = folder.SelectedPath;
                }
                primary.Text = alternateOutput == null ? "重试" : "保存到所选目录";
                primary.Visible = true;
                progress.Visible = false;
            }
            catch (Exception error)
            {
                if (IsDisposed) return;
                StatusLabel.Text = "识别未完成";
                details.Text = error.Message;
                primary.Text = "重试";
                primary.Visible = true;
                progress.Visible = false;
                notification.Visible = true;
                notification.ShowBalloonTip(5000, "MinerU 识别未完成", error.Message, ToolTipIcon.Warning);
            }
            finally
            {
                busy = false;
                if (!IsDisposed) settingsButton.Enabled = true;
            }
        }

        private void UpdateStatus(string state)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(UpdateStatus), state); return; }
            StatusLabel.Text = state;
        }

        private void OpenOutputDirectory()
        {
            if (outputs == null || outputs.Count == 0) return;
            Process.Start(new ProcessStartInfo("explorer.exe", "/select," + Program.QuoteArgument(outputs[0])) { UseShellExecute = true });
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(IntPtr window, string name, IntPtr value);
    }
}
