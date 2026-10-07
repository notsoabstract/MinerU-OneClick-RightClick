const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const os = require("node:os");
const vm = require("node:vm");
const JSZip = require("jszip");

async function fixture(t) {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "mineru-export-test-"));
  t.after(async () => {
    assert.ok(path.basename(root).startsWith("mineru-export-test-"));
    assert.equal(path.dirname(root), os.tmpdir());
    await fs.rm(root, { recursive: true, force: true });
  });
  const events = [];
  const handlers = new Map();
  const electron = {
    app: { whenReady: () => ({ then() {} }), on() {}, getPath: () => root },
    ipcMain: { handle: (name, handler) => handlers.set(name, handler) }
  };
  const context = vm.createContext({
    require: (name) => name === "electron" ? electron : require(name),
    __dirname: path.resolve(__dirname, "../src"),
    Buffer,
    setTimeout,
    events,
    fetch: async () => { throw new Error("Unexpected network request in local test"); }
  });
  vm.runInContext(await fs.readFile(path.join(__dirname, "../src/main.js"), "utf8"), context);
  vm.runInContext("mainWindow = { isDestroyed: () => false, webContents: { send: (channel, payload) => events.push({ channel, payload }) } }", context);
  const api = vm.runInContext("({ extractSelectedExports, copyExportBesideSource, DEFAULT_SETTINGS })", context);
  return { root, context, api, events, handlers };
}

async function resultZip(includeWord = true) {
  const zip = new JSZip();
  zip.file("nested/full.md", "# Recognized text");
  if (includeWord) zip.file("nested/document.docx", Buffer.from("word-export-fixture"));
  zip.file("nested/images/figure.png", Buffer.from("unused-image-fixture"));
  return zip.generateAsync({ type: "nodebuffer" });
}

test("existing edited Markdown and numbered results remain byte-for-byte unchanged", async (t) => {
  const { root, api } = await fixture(t);
  const archive = path.join(root, "archive.md");
  const source = path.join(root, "合同.pdf");
  await fs.writeFile(archive, "new recognition");
  await fs.writeFile(path.join(root, "合同.md"), "edited old text");
  await fs.writeFile(path.join(root, "合同_识别结果.md"), "previous recognition");
  const output = await api.copyExportBesideSource(archive, source, ".md");
  assert.equal(path.basename(output), "合同_识别结果_2.md");
  assert.equal(await fs.readFile(output, "utf8"), "new recognition");
  assert.equal(await fs.readFile(path.join(root, "合同.md"), "utf8"), "edited old text");
  assert.equal(await fs.readFile(path.join(root, "合同_识别结果.md"), "utf8"), "previous recognition");
});

test("Word export preserves the original Word input", async (t) => {
  const { root, api } = await fixture(t);
  const archive = path.join(root, "export.docx");
  const source = path.join(root, "合同.docx");
  await fs.writeFile(archive, "recognized Word");
  await fs.writeFile(source, "original Word");
  const output = await api.copyExportBesideSource(archive, source, ".docx");
  assert.equal(path.basename(output), "合同_识别结果.docx");
  assert.equal(await fs.readFile(source, "utf8"), "original Word");
  assert.equal(await fs.readFile(output, "utf8"), "recognized Word");
});

test("simultaneous exports get distinct names without replacing each other", async (t) => {
  const { root, api } = await fixture(t);
  const archive = path.join(root, "archive.md");
  await fs.writeFile(archive, "new text");
  const outputs = await Promise.all(Array.from({ length: 8 }, () =>
    api.copyExportBesideSource(archive, path.join(root, "合同.pdf"), ".md")));
  assert.equal(new Set(outputs).size, 8);
  for (const output of outputs) assert.equal(await fs.readFile(output, "utf8"), "new text");
});

test("all three export selections send the correct cloud request and save only selected files", async (t) => {
  for (const selection of [
    { exportMarkdown: true, exportWord: false },
    { exportMarkdown: false, exportWord: true },
    { exportMarkdown: true, exportWord: true }
  ]) {
    await t.test(JSON.stringify(selection), async (t) => {
      const { root, context, events, handlers, api } = await fixture(t);
      const source = path.join(root, "sample.xlsx");
      await fs.writeFile(source, "excel-input-fixture");
      const zipBuffer = await resultZip();
      const requests = [];
      context.fetch = async (url, options = {}) => {
        requests.push({ url, options });
        if (url.endsWith("/file-urls/batch")) return {
          ok: true, text: async () => JSON.stringify({ code: 0, data: { batch_id: "test-batch", file_urls: ["https://test.invalid/upload"] } })
        };
        if (url === "https://test.invalid/upload") return { ok: true };
        if (url.includes("/extract-results/batch/")) return {
          ok: true, text: async () => JSON.stringify({ code: 0, data: { extract_result: [{ state: "done", full_zip_url: "https://test.invalid/result.zip" }] } })
        };
        if (url === "https://test.invalid/result.zip") return { ok: true, arrayBuffer: async () => zipBuffer };
        throw new Error("Unexpected test URL");
      };
      await handlers.get("jobs:start")(null, [source], {
        ...api.DEFAULT_SETTINGS, token: "test-token", outputRoot: path.join(root, "projects"), ...selection
      });
      const createBody = JSON.parse(requests[0].options.body);
      assert.deepEqual(createBody.extra_formats, selection.exportWord ? ["docx"] : undefined);
      assert.equal(createBody.files[0].name, "sample.xlsx");
      const completion = events.find((event) => event.payload.state === "done")?.payload;
      assert.ok(completion, JSON.stringify(events));
      const sourceFiles = await fs.readdir(root);
      assert.equal(sourceFiles.includes("sample.md"), selection.exportMarkdown);
      assert.equal(sourceFiles.includes("sample.docx"), selection.exportWord);
      assert.equal(completion.sourceOutputPaths.length, Number(selection.exportMarkdown) + Number(selection.exportWord));
      const archivedFiles = await fs.readdir(completion.projectDir);
      assert.equal(archivedFiles.includes("full.md"), selection.exportMarkdown);
      assert.equal(archivedFiles.includes("full.docx"), selection.exportWord);
      assert.equal(archivedFiles.includes("images"), false);
    });
  }
});

test("missing requested Word produces a clear error rather than false completion", async (t) => {
  const { root, api } = await fixture(t);
  await assert.rejects(api.extractSelectedExports(await resultZip(false), root,
    { exportMarkdown: true, exportWord: true }), /没有 Word 文件/);
  assert.deepEqual(await fs.readdir(root), []);
});

test("Word archive never overwrites an original named full.docx", async (t) => {
  const { root, api } = await fixture(t);
  await fs.writeFile(path.join(root, "full.docx"), "original input");
  const exports = await api.extractSelectedExports(await resultZip(), root,
    { exportMarkdown: false, exportWord: true });
  assert.equal(await fs.readFile(path.join(root, "full.docx"), "utf8"), "original input");
  assert.equal(path.basename(exports[0].archivePath), "full_识别结果.docx");
});

test("no formats selected is rejected before any network request", async (t) => {
  const { handlers, events } = await fixture(t);
  await assert.rejects(handlers.get("jobs:start")(null, ["input.pdf"],
    { exportMarkdown: false, exportWord: false }), /至少开启/);
  assert.equal(events.at(-1).channel, "queue:done");
});

test("old settings keep Markdown enabled and Word disabled", async (t) => {
  const { root, handlers } = await fixture(t);
  await fs.writeFile(path.join(root, "settings.json"), JSON.stringify({ token: "old-test-token" }));
  const settings = await handlers.get("settings:load")();
  assert.equal(settings.exportMarkdown, true);
  assert.equal(settings.exportWord, false);
});
