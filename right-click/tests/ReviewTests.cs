using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MinerURightClick;

internal static class ReviewTests
{
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type SettingsType = typeof(ToolSettings).Assembly.GetType("MinerURightClick.SettingsForm", true);
    private static readonly Type WorkType = typeof(ToolSettings).Assembly.GetType("MinerURightClick.WorkForm", true);
    private static readonly Type ProgramType = typeof(ToolSettings).Assembly.GetType("MinerURightClick.Program", true);
    private static readonly List<string> Results = new List<string>();
    private static readonly List<string> LayoutIssues = new List<string>();
    private static string reportDirectory;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        reportDirectory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(reportDirectory);
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "MinerURightClickReview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            Run("Settings protected token roundtrip and replacement", delegate { CheckPersistence(temporaryDirectory); });
            Run("Settings MD / Word / both save selections", CheckSelections);
            Run("Settings empty token and no-format validation", CheckInvalidSelections);
            Run("Windows argument quoting roundtrip", CheckQuotes);
            Run("Job identity normalizes path and distinguishes directories", CheckIdentity);
            Run("Offscreen settings and task state captures", CaptureScreens);
            File.WriteAllLines(Path.Combine(reportDirectory, "ReviewTests-results.txt"), Results);
            Console.WriteLine(String.Join(Environment.NewLine, Results));
            return Results.Exists(s => s.StartsWith("FAIL ", StringComparison.Ordinal)) ? 1 : 0;
        }
        finally { Directory.Delete(temporaryDirectory, true); }
    }

    private static void Run(string name, Action test)
    {
        try { test(); Results.Add("PASS " + name); }
        catch (Exception error) { Results.Add("FAIL " + name + ": " + error.GetType().Name + " " + error.Message); }
    }

    private static void CheckPersistence(string temporaryDirectory)
    {
        string file = Path.Combine(temporaryDirectory, "ReviewSettings.json");
        const string fakeToken = "ReviewOnly-NonSecret-Token-81874";
        ToolSettings original = new ToolSettings { Token = " Bearer " + fakeToken + " ", ExportMarkdown = false, ExportWord = true };
        SettingsStore.SaveTo(file, original);
        Assert(original.Token == fakeToken, "Normalization did not update in-memory settings");
        Assert(!File.ReadAllText(file).Contains(fakeToken), "Settings contains clear token");
        ToolSettings roundtrip = SettingsStore.LoadFrom(file);
        Assert(roundtrip.Token == fakeToken && !roundtrip.ExportMarkdown && roundtrip.ExportWord, "Roundtrip mismatch");
        original.Token = fakeToken + "-Changed";
        original.ExportMarkdown = true;
        original.ExportWord = false;
        SettingsStore.SaveTo(file, original);
        roundtrip = SettingsStore.LoadFrom(file);
        Assert(roundtrip.Token == original.Token && roundtrip.ExportMarkdown && !roundtrip.ExportWord, "Atomic replacement mismatch");
        Assert(Directory.GetFiles(temporaryDirectory, "*.tmp").Length == 0, "Settings left a temp file");
        Assert(SettingsStore.NormalizeToken("  bearer abc  ") == "abc", "Bearer stripping failed");
    }

    private static Form CreateSettings(ToolSettings settings, Action<ToolSettings> saver)
    {
        return (Form)Activator.CreateInstance(SettingsType, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { settings, saver }, null);
    }

    private static T Field<T>(Type type, object instance, string field) where T : class
    { return (T)type.GetField(field, Hidden).GetValue(instance); }

    private static void CheckSelections()
    {
        foreach (int selection in new[] { 1, 2, 3 })
        {
            ToolSettings saved = null;
            using (Form form = CreateSettings(new ToolSettings { Token = "ReviewOnly" }, value => saved = value))
            {
                Field<CheckBox>(SettingsType, form, "MarkdownSwitch").Checked = (selection & 1) != 0;
                Field<CheckBox>(SettingsType, form, "WordSwitch").Checked = (selection & 2) != 0;
                SettingsType.GetMethod("SaveSelection", Hidden).Invoke(form, null);
                Assert(saved != null && saved.ExportMarkdown == ((selection & 1) != 0) && saved.ExportWord == ((selection & 2) != 0), "Selection not saved");
            }
        }
    }

    private static void CheckInvalidSelections()
    {
        using (Form form = CreateSettings(new ToolSettings { Token = "ReviewOnly" }, value => { }))
        {
            Field<CheckBox>(SettingsType, form, "MarkdownSwitch").Checked = false;
            Field<CheckBox>(SettingsType, form, "WordSwitch").Checked = false;
            ExpectValidation(form);
            Field<CheckBox>(SettingsType, form, "MarkdownSwitch").Checked = true;
            Field<TextBox>(SettingsType, form, "TokenInput").Text = " ";
            ExpectValidation(form);
        }
    }

    private static void ExpectValidation(Form form)
    {
        try { SettingsType.GetMethod("ReadSelection", Hidden).Invoke(form, null); }
        catch (TargetInvocationException error)
        { if (error.InnerException is InvalidOperationException) return; throw; }
        throw new Exception("Expected validation failure");
    }

    private static string ProgramCall(string name, string value)
    { return (string)ProgramType.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { value }); }

    private static void CheckQuotes()
    {
        string[] samples = { "", "C:\\文件 夹\\合同.pdf", "C:\\folder\\", "a\"b", "a\\\\\"b", "value & (name) %1 $x ' end", "日本語😀.xlsx" };
        foreach (string sample in samples)
        {
            int count;
            IntPtr buffer = CommandLineToArgvW("ReviewTests.exe " + ProgramCall("QuoteArgument", sample), out count);
            try
            {
                Assert(count == 2, "Quoted argument split into an unexpected count");
                string decoded = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, IntPtr.Size));
                Assert(decoded == sample, "Quoted argument changed value");
            }
            finally { LocalFree(buffer); }
        }
    }

    private static void CheckIdentity()
    {
        string first = ProgramCall("JobKey", @"C:\Review\A\合同.pdf");
        Assert(first.Length == 64, "Key was not a SHA256 hex digest");
        Assert(first == ProgramCall("JobKey", @"c:\review\a\合同.PDF"), "Same path casing changes job");
        Assert(first == ProgramCall("JobKey", @"C:\Review\A\..\A\合同.pdf"), "Path segment normalization failed");
        Assert(first != ProgramCall("JobKey", @"C:\Review\B\合同.pdf"), "Different directory shares job");
    }

    private static void CaptureScreens()
    {
        using (Form form = CreateSettings(new ToolSettings { Token = "ReviewOnly-Synthetic", ExportMarkdown = true, ExportWord = true }, value => { }))
        {
            Assert(Field<TextBox>(SettingsType, form, "TokenInput").UseSystemPasswordChar, "Token display is unmasked");
            Capture(form, "ReviewSettings-200.png", 1f);
        }
        foreach (float scale in new[] { .5f, .625f, .75f })
            using (Form form = CreateSettings(new ToolSettings { Token = "ReviewOnly-Synthetic", ExportMarkdown = true, ExportWord = true }, value => { }))
                Capture(form, "ReviewSettings-" + (scale * 200).ToString("0") + ".png", scale);
        using (Form form = CreateWork())
        {
            Field<Label>(WorkType, form, "StatusLabel").Text = "识别中：3 / 8 页";
            Capture(form, "ReviewProgress-200.png", 1f);
        }
        using (Form form = CreateWork())
        {
            Field<Label>(WorkType, form, "StatusLabel").Text = "识别完成";
            Field<Label>(WorkType, form, "details").Text = "已保存：测试合同_识别结果.md、测试合同_识别结果.docx";
            Button open = Field<Button>(WorkType, form, "primary");
            open.Text = "打开结果目录";
            open.Visible = true;
            Field<Button>(WorkType, form, "againButton").Visible = true;
            Field<ProgressBar>(WorkType, form, "progress").Visible = false;
            Capture(form, "ReviewComplete-200.png", 1f);
        }
        if (LayoutIssues.Count > 0) throw new Exception(String.Join("; ", LayoutIssues));
    }

    private static Form CreateWork()
    {
        // Constructor/DrawToBitmap only: never Show(), so no Shown handler or cloud call runs.
        return (Form)Activator.CreateInstance(WorkType, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { @"C:\Review\测试合同.pdf", "REVIEW-SYNTHETIC-JOB" }, null);
    }

    private static void Capture(Form form, string name, float scale)
    {
        typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(bool) }, null).Invoke(form, new object[] { true });
        if (scale != 1f)
        {
            var originalFonts = new Dictionary<Control, Font>();
            foreach (Control child in form.Controls) originalFonts.Add(child, child.Font);
            form.Scale(new SizeF(scale, scale));
            form.Font = new Font(form.Font.FontFamily, form.Font.Size * scale, form.Font.Style);
            foreach (var pair in originalFonts) pair.Key.Font = new Font(pair.Value.FontFamily, pair.Value.Size * scale, pair.Value.Style);
        }
        form.PerformLayout();
        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            using (Graphics canvas = Graphics.FromImage(bitmap))
            {
                Control[] children = new Control[form.Controls.Count];
                form.Controls.CopyTo(children, 0);
                MethodInfo getState = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic);
                Point clientOrigin = form.PointToScreen(Point.Empty);
                Point offset = new Point(clientOrigin.X - form.Left, clientOrigin.Y - form.Top);
                foreach (Control child in children)
                {
                    bool individuallyVisible = (bool)getState.Invoke(child, new object[] { 2 });
                    if (!individuallyVisible) continue;
                    Point location = child.Location;
                    Size childSize = child.Size;
                    if (location.X + childSize.Width > form.ClientSize.Width || location.Y + childSize.Height > form.ClientSize.Height)
                        LayoutIssues.Add(name + " control outside form: " + child.Text + " bounds=" + new Rectangle(location, childSize) + " client=" + form.ClientSize);
                    if (!(child is ProgressBar) && childSize.Height < (int)Math.Ceiling(child.Font.GetHeight()))
                        LayoutIssues.Add(name + " control height clips text: " + child.Text);
                    Font inheritedFont = child.Font;
                    child.Parent = null;
                    child.Font = inheritedFont;
                    child.AutoSize = false;
                    child.Size = childSize;
                    child.Visible = true;
                    using (Bitmap controlBitmap = new Bitmap(child.Width, child.Height))
                    {
                        child.DrawToBitmap(controlBitmap, new Rectangle(Point.Empty, controlBitmap.Size));
                        canvas.DrawImageUnscaled(controlBitmap, location.X + offset.X, location.Y + offset.Y);
                    }
                    child.Dispose();
                }
            }
            bitmap.Save(Path.Combine(reportDirectory, name), ImageFormat.Png);
        }
        Results.Add("INFO " + name + " form=" + form.ClientSize + " autoscale=" + form.AutoScaleMode);
    }

    private static void Assert(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static Control[] GetControls(Form form)
    { Control[] controls = new Control[form.Controls.Count]; form.Controls.CopyTo(controls, 0); return controls; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CommandLineToArgvW(string command, out int argc);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
