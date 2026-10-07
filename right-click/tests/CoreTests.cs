using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MinerURightClick;

#if CORE_TEST_STUB
namespace MinerURightClick
{
    public class ToolSettings
    {
        public string Token { get; set; }
        public bool ExportMarkdown { get; set; }
        public bool ExportWord { get; set; }
    }
}
#endif

public static class CoreTests
{
    private static int passed;
    private static string root;
    public static int Main()
    {
        root = Path.Combine(Path.GetTempPath(), "MinerURightClickCoreTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Run().GetAwaiter().GetResult();
            Console.WriteLine("PASS: " + passed + " assertions; no real network requests or user documents used.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
        finally { Directory.Delete(root, true); }
    }

    private static async Task Run()
    {
        Assert(RecognitionRunner.IsSupportedPath("测试.XLSX"), "Excel accepted");
        Assert(RecognitionRunner.IsSupportedPath("image.JP2"), "JPEG2000 accepted");
        Assert(!RecognitionRunner.IsSupportedPath("source.exe"), "Executable rejected");
        await MarkdownAndCollision();
        await WordOnlyAndBoth();
        await CancellationAndResume();
        await ExportFailureAndResume();
        await MissingFormatNoPartialOutputs();
        await FailedTaskStartsNewBatch();
        await CorruptCachedResultRefreshes();
        await RequestTimeout();
    }

    private static string Folder(string name) { string path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
    private static string Source(string directory) { string path = Path.Combine(directory, "测试.pdf"); File.WriteAllText(path, "Synthetic test document; no private data."); return path; }
    private static ToolSettings Settings(bool md, bool word) { return new ToolSettings { Token = "synthetic-token", ExportMarkdown = md, ExportWord = word }; }
    private static RecognitionRunner Runner(MockHandler mock, string directory) { return new RecognitionRunner(mock, Path.Combine(directory, "cache"), TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(10)); }

    private static async Task MarkdownAndCollision()
    {
        string directory = Folder("md");
        string source = Source(directory);
        string oldResult = Path.Combine(directory, "测试_识别结果.md");
        File.WriteAllText(oldResult, "Existing edited result");
        File.WriteAllText(Path.Combine(directory, "测试_识别结果_2.md"), "Existing second result");
        File.WriteAllText(Path.Combine(directory, "测试.md"), "Original markdown");
        var handler = new MockHandler();
        using (var runner = Runner(handler, directory))
        {
            var result = await runner.ProcessAsync(source, Settings(true, false), null, CancellationToken.None);
            Assert(result.Count == 1 && result[0].EndsWith("测试_识别结果_3.md"), "Collision receives next suffix");
            Assert(File.ReadAllText(oldResult) == "Existing edited result", "Existing result retained");
            Assert(File.ReadAllText(Path.Combine(directory, "测试.md")) == "Original markdown", "Original markdown retained");
            Assert(File.ReadAllText(result[0]) == "# Synthetic recognized content", "full.md selected over other markdown");
            Assert(!Directory.Exists(Path.Combine(directory, "images")), "Images not exported");
            Assert(!Directory.EnumerateFiles(Path.Combine(directory, "cache")).Any(), "Success clears job and zip");
            Assert(!handler.LastBody.Contains("extra_formats"), "MD only omits extra formats");
        }
    }

    private static async Task WordOnlyAndBoth()
    {
        string directory = Folder("word");
        string source = Source(directory);
        var handler = new MockHandler();
        using (var runner = Runner(handler, directory))
        {
            var result = await runner.ProcessAsync(source, Settings(false, true), null, CancellationToken.None);
            Assert(result.Count == 1 && result[0].EndsWith(".docx"), "Word only exports one docx");
            Assert(!Directory.EnumerateFiles(directory, "*.md").Any(), "Word only exports no markdown");
            Assert(handler.LastBody.Contains("\"extra_formats\":[\"docx\"]"), "Word requested from API");
            var body = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(handler.LastBody);
            Assert((string)body["model_version"] == "vlm" && (string)body["language"] == "ch" && (bool)body["enable_table"] && (bool)body["enable_formula"], "Cloud options set");
            var files = (System.Collections.ArrayList)body["files"];
            Assert((bool)((Dictionary<string, object>)files[0])["is_ocr"], "OCR set per file");
            result = await runner.ProcessAsync(source, Settings(true, true), null, CancellationToken.None);
            Assert(result.Count == 2 && result.Any(p => p.EndsWith(".md")) && result.Any(p => p.EndsWith("_2.docx")), "Both formats and collision supported");
        }
    }

    private static async Task CancellationAndResume()
    {
        string directory = Folder("resume");
        string source = Source(directory);
        var handler = new MockHandler();
        handler.Pending = true;
        using (var cancellation = new CancellationTokenSource())
        using (var runner = Runner(handler, directory))
        {
            bool cancelled = false;
            try { await runner.ProcessAsync(source, Settings(true, false), delegate(string text) { if (text.StartsWith("排队")) cancellation.Cancel(); }, cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "Cancellation stops polling");
            Assert(handler.CreateCount == 1 && handler.UploadCount == 1, "First run submitted exactly once");
            Assert(Directory.EnumerateFiles(Path.Combine(directory, "cache"), "*.job").Count() == 1, "Cancelled task retained");
            handler.Pending = false;
            var result = await runner.ProcessAsync(source, Settings(true, false), null, CancellationToken.None);
            Assert(result.Count == 1 && handler.CreateCount == 1 && handler.UploadCount == 1, "Retry resumes saved batch without another upload");
        }
    }

    private static async Task ExportFailureAndResume()
    {
        string directory = Folder("save-retry");
        string source = Source(directory);
        string blocked = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(blocked, "Synthetic obstacle");
        var handler = new MockHandler();
        using (var runner = Runner(handler, directory))
        {
            bool caught = false;
            try { await runner.ProcessAsync(source, Settings(true, true), null, CancellationToken.None, blocked); }
            catch (ExportPermissionException ex) { caught = ex.DirectoryPath == blocked; }
            Assert(caught, "Save failure has dedicated exception");
            Assert(Directory.EnumerateFiles(Path.Combine(directory, "cache"), "*.zip").Count() == 1, "Downloaded result retained after save failure");
            int requests = handler.Count;
            string alternative = Folder("alternative");
            var result = await runner.ProcessAsync(source, Settings(true, true), null, CancellationToken.None, alternative);
            Assert(result.Count == 2 && handler.Count == requests, "Choosing another folder uses cached results without network");
            Assert(!Directory.EnumerateFiles(alternative, "*.tmp").Any(), "Save leaves no temporary files");
        }
    }

    private static async Task MissingFormatNoPartialOutputs()
    {
        string directory = Folder("missing-word");
        string source = Source(directory);
        var handler = new MockHandler();
        handler.IncludeWord = false;
        using (var runner = Runner(handler, directory))
        {
            bool failed = false;
            try { await runner.ProcessAsync(source, Settings(true, true), null, CancellationToken.None); }
            catch (InvalidOperationException ex) { failed = ex.Message.Contains("没有 Word"); }
            Assert(failed, "Missing Word has concrete error");
            Assert(!Directory.EnumerateFiles(directory, "*识别结果*").Any(), "Missing selected format leaves no partial export");
            Assert(!Directory.EnumerateFiles(Path.Combine(directory, "cache"), "*.zip").Any(), "Missing selected format discards stale zip");
            handler.IncludeWord = true;
            var result = await runner.ProcessAsync(source, Settings(true, true), null, CancellationToken.None);
            Assert(result.Count == 2 && handler.CreateCount == 1 && handler.UploadCount == 1, "Missing format retry refreshes completed result without resubmission");
        }
    }

    private static async Task FailedTaskStartsNewBatch()
    {
        string directory = Folder("failed-batch");
        string source = Source(directory);
        var handler = new MockHandler();
        handler.Failed = true;
        using (var runner = Runner(handler, directory))
        {
            bool failed = false;
            try { await runner.ProcessAsync(source, Settings(true, false), null, CancellationToken.None); }
            catch (InvalidOperationException ex) { failed = ex.Message.Contains("synthetic failure"); }
            Assert(failed && !Directory.EnumerateFiles(Path.Combine(directory, "cache")).Any(), "Terminal cloud failure discards failed batch");
            handler.Failed = false;
            var result = await runner.ProcessAsync(source, Settings(true, false), null, CancellationToken.None);
            Assert(result.Count == 1 && handler.CreateCount == 2 && handler.UploadCount == 2, "Failed task retry creates a fresh batch");
        }
    }

    private static async Task RequestTimeout()
    {
        string directory = Folder("timeout");
        string source = Source(directory);
        var handler = new MockHandler();
        handler.DelayCreate = true;
        using (var runner = new RecognitionRunner(handler, Path.Combine(directory, "cache"), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(20)))
        {
            bool timedOut = false;
            try { await runner.ProcessAsync(source, Settings(true, false), null, CancellationToken.None); }
            catch (TimeoutException) { timedOut = true; }
            Assert(timedOut && handler.CreateCount == 1, "Ambiguous creation timeout not automatically resubmitted");
        }
    }

    private static async Task CorruptCachedResultRefreshes()
    {
        string directory = Folder("corrupt-cache");
        string source = Source(directory);
        var handler = new MockHandler();
        handler.Pending = true;
        using (var runner = Runner(handler, directory))
        using (var cancellation = new CancellationTokenSource())
        {
            try { await runner.ProcessAsync(source, Settings(true, false), delegate(string text) { if (text.StartsWith("排队")) cancellation.Cancel(); }, cancellation.Token); }
            catch (OperationCanceledException) { }
            string manifest = Directory.EnumerateFiles(Path.Combine(directory, "cache"), "*.job").Single();
            string zip = Path.ChangeExtension(manifest, ".zip");
            File.WriteAllText(zip, "Synthetic corrupt zip");
            bool failed = false;
            try { await runner.ProcessAsync(source, Settings(true, false), null, CancellationToken.None); }
            catch (InvalidOperationException ex) { failed = ex.Message.Contains("结果包已损坏"); }
            Assert(failed && !File.Exists(zip), "Corrupt cached result discarded with useful error");
            handler.Pending = false;
            var result = await runner.ProcessAsync(source, Settings(true, false), null, CancellationToken.None);
            Assert(result.Count == 1 && handler.CreateCount == 1 && handler.UploadCount == 1, "Corrupt result retry downloads without resubmission");
        }
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); passed++; }

    private sealed class MockHandler : HttpMessageHandler
    {
        public int CreateCount, UploadCount, Count;
        public string LastBody;
        public bool Pending, DelayCreate, Failed;
        public bool IncludeWord = true;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            if (request.Method == HttpMethod.Post)
            {
                CreateCount++;
                if (DelayCreate) await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
                LastBody = await request.Content.ReadAsStringAsync();
                Assert(request.Headers.Authorization.ToString() == "Bearer synthetic-token", "Token sent only to API");
                return Json("{\"code\":0,\"data\":{\"batch_id\":\"synthetic-batch\",\"file_urls\":[\"https://synthetic.test/upload\"]}}");
            }
            if (request.Method == HttpMethod.Put)
            {
                UploadCount++;
                Assert(request.Headers.Authorization == null, "Token omitted from signed upload");
                Assert((await request.Content.ReadAsStringAsync()).Contains("Synthetic test"), "Selected source uploaded");
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (request.RequestUri.Host == "mineru.net")
            {
                if (Failed) return Json("{\"code\":0,\"data\":{\"extract_result\":[{\"state\":\"failed\",\"err_msg\":\"synthetic failure\"}]}}");
                return Json(Pending ? "{\"code\":0,\"data\":{\"extract_result\":[{\"state\":\"pending\"}]}}" : "{\"code\":0,\"data\":{\"extract_result\":[{\"state\":\"done\",\"full_zip_url\":\"https://synthetic.test/result.zip\"}]}}");
            }
            Assert(request.Headers.Authorization == null, "Token omitted from result download");
            using (var bytes = new MemoryStream())
            {
                using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, true))
                {
                    Entry(archive, "nested/other.md", "Wrong markdown");
                    Entry(archive, "full.md", "# Synthetic recognized content");
                    Entry(archive, "images/ignored.png", "Synthetic image bytes");
                    if (IncludeWord) Entry(archive, "full.docx", "Synthetic Word output bytes");
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes.ToArray()) };
            }
        }
        private static HttpResponseMessage Json(string text) { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") }; }
        private static void Entry(ZipArchive archive, string name, string text) { using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(text); }
    }
}
