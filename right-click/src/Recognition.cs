using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MinerURightClick
{
    public sealed class ExportPermissionException : Exception
    {
        public string DirectoryPath { get; private set; }
        public ExportPermissionException(string directoryPath, Exception inner)
            : base("无法把结果保存到此文件夹，请选择其他保存位置。识别结果已经保留，无须重新上传。", inner)
        { DirectoryPath = directoryPath; }
    }

    public sealed class RecognitionRunner : IDisposable
    {
        private const string ApiBase = "https://mineru.net/api/v4/";
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(
            new[] { ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg", ".jp2", ".webp", ".gif", ".bmp" }, StringComparer.OrdinalIgnoreCase);
        private readonly HttpClient client;
        private readonly string cacheDirectory;
        private readonly TimeSpan pollInterval;
        private readonly TimeSpan requestTimeout;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

        public RecognitionRunner()
            : this(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MinerURightClick", "jobs"), TimeSpan.FromSeconds(4), TimeSpan.FromMinutes(5)) { }

        internal RecognitionRunner(HttpMessageHandler handler, string cacheDirectory, TimeSpan pollInterval, TimeSpan requestTimeout)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            this.client = handler == null ? new HttpClient() : new HttpClient(handler);
            this.client.Timeout = Timeout.InfiniteTimeSpan;
            this.cacheDirectory = cacheDirectory;
            this.pollInterval = pollInterval;
            this.requestTimeout = requestTimeout;
        }

        public static bool IsSupportedPath(string path)
        {
            try { return !String.IsNullOrWhiteSpace(path) && SupportedExtensions.Contains(Path.GetExtension(path)); }
            catch (ArgumentException) { return false; }
        }

        public async Task<List<string>> ProcessAsync(string sourcePath, ToolSettings settings, Action<string> progress, CancellationToken cancellationToken, string saveDirectory = null)
        {
            if (settings == null || String.IsNullOrWhiteSpace(settings.Token)) throw new InvalidOperationException("请先填写 MinerU API Token。");
            if (!settings.ExportMarkdown && !settings.ExportWord) throw new InvalidOperationException("请至少选择一种导出格式。");
            sourcePath = Path.GetFullPath(sourcePath);
            if (!IsSupportedPath(sourcePath)) throw new InvalidOperationException("此文件类型暂不支持。请选择 PDF、Word、PPT、Excel 或图片。");
            var info = new FileInfo(sourcePath);
            if (!info.Exists) throw new FileNotFoundException("找不到待识别文件。文件可能已经移动或删除。", sourcePath);
            if (info.Length == 0) throw new InvalidOperationException("文件是空的，无法识别。");
            if (info.Length > 200L * 1024 * 1024) throw new InvalidOperationException("文件超过 MinerU 的 200 MB 限制。");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(cacheDirectory);
            string fingerprint = Hash(sourcePath.ToUpperInvariant() + "|" + info.Length.ToString(CultureInfo.InvariantCulture) + "|" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + settings.ExportWord + "|" + Hash(settings.Token.Trim()));
            string manifestPath = Path.Combine(cacheDirectory, fingerprint + ".job");
            string zipPath = Path.Combine(cacheDirectory, fingerprint + ".zip");
            JobManifest job = LoadJob(manifestPath);
            if (job == null || DateTime.UtcNow - job.CreatedUtc > TimeSpan.FromDays(6))
            {
                DeleteQuietly(zipPath);
                job = new JobManifest { CreatedUtc = DateTime.UtcNow, DataId = "rc_" + Guid.NewGuid().ToString("N") };
            }
            if (String.IsNullOrEmpty(job.BatchId))
            {
                Report(progress, "申请上传地址…");
                var file = new Dictionary<string, object> { { "name", info.Name }, { "data_id", job.DataId }, { "is_ocr", true } };
                var body = new Dictionary<string, object> {
                    { "files", new object[] { file } }, { "model_version", "vlm" },
                    { "language", "ch" }, { "enable_formula", true }, { "enable_table", true }
                };
                if (settings.ExportWord) body["extra_formats"] = new[] { "docx" };
                // Creation has no idempotency guarantee. Do not retry an ambiguous POST automatically.
                var response = await RequestJsonAsync(HttpMethod.Post, ApiBase + "file-urls/batch", settings.Token, json.Serialize(body), 1, cancellationToken).ConfigureAwait(false);
                var data = GetObject(response, "data");
                job.BatchId = GetString(data, "batch_id");
                var urls = GetArray(data, "file_urls");
                job.UploadUrl = urls.Count > 0 ? UploadUrl(urls[0]) : "";
                if (String.IsNullOrEmpty(job.BatchId) || String.IsNullOrEmpty(job.UploadUrl)) throw new InvalidOperationException("MinerU 未返回有效的上传地址，请稍后重试。");
                SaveJob(manifestPath, job);
            }
            else Report(progress, "继续上次识别…");

            if (!job.Uploaded)
            {
                if (DateTime.UtcNow - job.CreatedUtc > TimeSpan.FromHours(23))
                {
                    DeleteQuietly(manifestPath);
                    throw new InvalidOperationException("上次上传地址已过期，请点击重试重新提交。");
                }
                Report(progress, "上传文件…");
                await UploadAsync(job.UploadUrl, sourcePath, cancellationToken).ConfigureAwait(false);
                job.Uploaded = true;
                SaveJob(manifestPath, job);
            }

            if (!File.Exists(zipPath))
            {
                string resultUrl;
                try { resultUrl = await PollAsync(job, settings.Token, progress, cancellationToken).ConfigureAwait(false); }
                catch (ParsingFailedException)
                {
                    DeleteQuietly(manifestPath);
                    DeleteQuietly(zipPath);
                    throw;
                }
                Report(progress, "下载识别结果…");
                await DownloadAsync(resultUrl, zipPath, cancellationToken).ConfigureAwait(false);
            }
            Report(progress, "保存结果…");
            string outputDirectory = String.IsNullOrWhiteSpace(saveDirectory) ? info.DirectoryName : Path.GetFullPath(saveDirectory);
            List<string> outputs;
            try
            {
                outputs = await ExportSelectedAsync(zipPath, outputDirectory, Path.GetFileNameWithoutExtension(sourcePath), settings, cancellationToken).ConfigureAwait(false);
            }
            catch (MissingResultFormatException)
            {
                // A format conversion can appear after the first result download.
                DeleteQuietly(zipPath);
                throw;
            }
            catch (InvalidDataException ex)
            {
                DeleteQuietly(zipPath);
                throw new InvalidOperationException("识别结果包已损坏，请点击重试重新下载，无须重新上传。", ex);
            }
            catch (UnauthorizedAccessException ex) { throw new ExportPermissionException(outputDirectory, ex); }
            catch (IOException ex)
            {
                throw new ExportPermissionException(outputDirectory, ex);
            }
            DeleteQuietly(zipPath);
            DeleteQuietly(manifestPath);
            Report(progress, "识别完成");
            return outputs;
        }

        private async Task<string> PollAsync(JobManifest job, string token, Action<string> progress, CancellationToken cancellationToken)
        {
            DateTime started = DateTime.UtcNow;
            while (DateTime.UtcNow - started < TimeSpan.FromHours(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await RequestJsonAsync(HttpMethod.Get, ApiBase + "extract-results/batch/" + Uri.EscapeDataString(job.BatchId), token, null, 3, cancellationToken).ConfigureAwait(false);
                var records = GetArray(GetObject(result, "data"), "extract_result");
                if (records.Count == 0) records = GetArray(GetObject(result, "data"), "extract_results");
                Dictionary<string, object> item = null;
                foreach (var entry in records)
                {
                    var row = entry as Dictionary<string, object>;
                    if (row != null && (GetString(row, "data_id") == job.DataId || records.Count == 1)) { item = row; break; }
                }
                if (item != null)
                {
                    string state = GetString(item, "state");
                    if (state == "done")
                    {
                        string url = GetString(item, "full_zip_url");
                        if (String.IsNullOrEmpty(url)) throw new InvalidOperationException("识别已完成，但 MinerU 未提供下载地址。请稍后重试。");
                        return url;
                    }
                    if (state == "failed") throw new ParsingFailedException("MinerU 识别失败：" + GetString(item, "err_msg") + "。点击重试会重新提交。");
                    if (state == "running")
                    {
                        var pages = GetObject(item, "extract_progress");
                        string total = GetString(pages, "total_pages");
                        string done = GetString(pages, "extracted_pages");
                        Report(progress, String.IsNullOrEmpty(total) ? "识别中…" : "识别中：" + done + " / " + total + " 页");
                    }
                    else if (state == "converting") Report(progress, "转换 Word 格式…");
                    else if (state == "waiting-file") Report(progress, "等待 MinerU 接收文件…");
                    else Report(progress, "排队中…");
                }
                else Report(progress, "排队中…");
                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
            throw new TimeoutException("MinerU 仍未返回结果。任务已经保留，点击重试会继续查询，无须重新上传。");
        }

        private async Task<Dictionary<string, object>> RequestJsonAsync(HttpMethod method, string url, string token, string body, int attempts, CancellationToken cancellationToken)
        {
            return await RetryAsync(async delegate(CancellationToken requestToken)
            {
                using (var request = new HttpRequestMessage(method, url))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
                    if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, requestToken).ConfigureAwait(false))
                    {
                        CheckHttp(response.StatusCode);
                        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        Dictionary<string, object> result;
                        try { result = json.Deserialize<Dictionary<string, object>>(text); }
                        catch (Exception ex) { throw new InvalidOperationException("MinerU 返回的数据无法读取，请稍后重试。", ex); }
                        if (result == null) throw new InvalidOperationException("MinerU 返回空数据，请稍后重试。");
                        string code = GetString(result, "code");
                        if (code != "0") throw new InvalidOperationException("MinerU 请求失败：" + GetString(result, "msg") + (String.IsNullOrEmpty(code) ? "" : "（" + code + "）"));
                        return result;
                    }
                }
            }, attempts, cancellationToken).ConfigureAwait(false);
        }

        private async Task UploadAsync(string url, string sourcePath, CancellationToken cancellationToken)
        {
            await RetryAsync(async delegate(CancellationToken requestToken)
            {
                using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var request = new HttpRequestMessage(HttpMethod.Put, SecureUri(url)))
                {
                    request.Content = new StreamContent(input);
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, requestToken).ConfigureAwait(false)) CheckHttp(response.StatusCode);
                }
                return true;
            }, 3, cancellationToken).ConfigureAwait(false);
        }

        private async Task DownloadAsync(string url, string zipPath, CancellationToken cancellationToken)
        {
            string temporary = zipPath + ".download";
            try
            {
                await RetryAsync(async delegate(CancellationToken requestToken)
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, SecureUri(url)))
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false))
                    {
                        CheckHttp(response.StatusCode);
                        using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                            await source.CopyToAsync(destination, 81920, requestToken).ConfigureAwait(false);
                    }
                    return true;
                }, 3, cancellationToken).ConfigureAwait(false);
                using (var archive = ZipFile.OpenRead(temporary))
                    if (archive.Entries.Count == 0) throw new InvalidOperationException("下载到的识别结果为空，请稍后重试。");
                File.Move(temporary, zipPath);
            }
            catch (InvalidDataException ex) { throw new InvalidOperationException("下载到的结果包不完整，请点击重试重新下载。", ex); }
            finally { DeleteQuietly(temporary); }
        }

        private async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> operation, int attempts, CancellationToken cancellationToken)
        {
            for (int attempt = 1; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool timedOut = false;
                Exception failure = null;
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    linked.CancelAfter(requestTimeout);
                    try { return await operation(linked.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException ex)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        timedOut = true; failure = ex;
                    }
                    catch (HttpRequestException ex) { failure = ex; }
                    catch (TransientHttpException ex) { failure = ex; }
                }
                if (attempt >= attempts)
                {
                    if (timedOut) throw new TimeoutException("网络请求超时。请检查网络后点击重试；已提交的任务会继续查询。", failure);
                    throw new InvalidOperationException("无法连接 MinerU。请检查网络后点击重试。", failure);
                }
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken).ConfigureAwait(false);
            }
        }

        internal static async Task<List<string>> ExportSelectedAsync(string zipPath, string outputDirectory, string sourceName, ToolSettings settings, CancellationToken cancellationToken)
        {
            var outputs = new List<string>();
            var pending = new List<string>();
            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var selected = new List<ZipArchiveEntry>();
                    if (settings.ExportMarkdown) selected.Add(SelectEntry(archive, ".md", sourceName));
                    if (settings.ExportWord) selected.Add(SelectEntry(archive, ".docx", sourceName));
                    Directory.CreateDirectory(outputDirectory);
                    foreach (var entry in selected)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string temporary = Path.Combine(outputDirectory, ".mineru-" + Guid.NewGuid().ToString("N") + ".tmp");
                        pending.Add(temporary);
                        using (var input = entry.Open())
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                            await input.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
                    }
                    for (int index = 0; index < selected.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string extension = Path.GetExtension(selected[index].Name).ToLowerInvariant();
                        string stem = sourceName + "_识别结果";
                        int suffix = 1;
                        while (true)
                        {
                            string destination = Path.Combine(outputDirectory, stem + (suffix == 1 ? "" : "_" + suffix.ToString(CultureInfo.InvariantCulture)) + extension);
                            try { File.Move(pending[index], destination); outputs.Add(destination); break; }
                            catch (IOException) { if (!File.Exists(destination)) throw; suffix++; }
                        }
                    }
                }
                return outputs;
            }
            catch
            {
                // An incomplete pair must not leave a surprise duplicate when the user retries.
                foreach (string output in outputs) DeleteQuietly(output);
                throw;
            }
            finally { foreach (string temporary in pending) DeleteQuietly(temporary); }
        }

        private static ZipArchiveEntry SelectEntry(ZipArchive archive, string extension, string sourceName)
        {
            var entries = archive.Entries.Where(e => e.Length > 0 && Path.GetExtension(e.Name).Equals(extension, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Name.Equals("full" + extension, StringComparison.OrdinalIgnoreCase) ? 0 : e.Name.Equals(sourceName + extension, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(e => e.FullName.Length).ToList();
            if (entries.Count == 0) throw new MissingResultFormatException("结果包中没有 " + (extension == ".docx" ? "Word" : "Markdown") + " 文件。MinerU 可能尚未完成格式转换，请稍后重试。");
            return entries[0];
        }

        private JobManifest LoadJob(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                byte[] clear = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                return json.Deserialize<JobManifest>(Encoding.UTF8.GetString(clear));
            }
            catch (CryptographicException) { return null; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private void SaveJob(string path, JobManifest job)
        {
            string temporary = path + ".new";
            try
            {
                byte[] encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(json.Serialize(job)), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(temporary, encrypted);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { DeleteQuietly(temporary); }
        }

        private static string Hash(string text)
        {
            using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        private static void Report(Action<string> progress, string text) { if (progress != null) progress(text); }
        private static void DeleteQuietly(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        private static Uri SecureUri(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("MinerU 返回的文件地址无效。");
            return uri;
        }
        private static void CheckHttp(HttpStatusCode status)
        {
            int code = (int)status;
            if (code >= 200 && code <= 299) return;
            if (code == 408 || code == 429 || code >= 500) throw new TransientHttpException(code);
            if (code == 401 || code == 403) throw new InvalidOperationException("MinerU 拒绝访问。请检查 Token 是否正确、是否已过期，或账号是否有接口权限。");
            throw new InvalidOperationException("MinerU 请求失败（HTTP " + code.ToString(CultureInfo.InvariantCulture) + "），请稍后重试。");
        }
        private static Dictionary<string, object> GetObject(Dictionary<string, object> source, string key)
        { object value; return source != null && source.TryGetValue(key, out value) ? value as Dictionary<string, object> ?? new Dictionary<string, object>() : new Dictionary<string, object>(); }
        private static string GetString(Dictionary<string, object> source, string key)
        { object value; return source != null && source.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : ""; }
        private static List<object> GetArray(Dictionary<string, object> source, string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value))
            {
                var array = value as object[];
                if (array != null) return array.ToList();
                var list = value as System.Collections.ArrayList;
                if (list != null) return list.Cast<object>().ToList();
            }
            return new List<object>();
        }
        private static string UploadUrl(object value)
        {
            var text = value as string;
            if (text != null) return text;
            var item = value as Dictionary<string, object>;
            return item == null ? "" : new[] { "url", "upload_url", "file_url" }.Select(k => GetString(item, k)).FirstOrDefault(v => !String.IsNullOrEmpty(v)) ?? "";
        }
        public void Dispose() { client.Dispose(); }
        private sealed class TransientHttpException : Exception
        { public TransientHttpException(int status) : base("HTTP " + status.ToString(CultureInfo.InvariantCulture)) { } }
        private sealed class ParsingFailedException : InvalidOperationException
        { public ParsingFailedException(string message) : base(message) { } }
        private sealed class MissingResultFormatException : InvalidOperationException
        { public MissingResultFormatException(string message) : base(message) { } }
        public sealed class JobManifest
        {
            public DateTime CreatedUtc { get; set; }
            public string BatchId { get; set; }
            public string DataId { get; set; }
            public string UploadUrl { get; set; }
            public bool Uploaded { get; set; }
        }
    }
}
