const { app, BrowserWindow, ipcMain, dialog, shell, Menu } = require("electron");
const path = require("node:path");
const fs = require("node:fs/promises");
const crypto = require("node:crypto");
const JSZip = require("jszip");

const API_BASE = "https://mineru.net";
const DEFAULT_SETTINGS = {
  token: "",
  outputRoot: "D:\\MinerU",
  modelVersion: "vlm",
  language: "ch",
  isOcr: true,
  enableTable: true,
  enableFormula: true,
  exportMarkdown: true,
  exportWord: false,
  pollIntervalSeconds: 10,
  timeoutMinutes: 60
};

let mainWindow;
let isRunning = false;

function settingsPath() {
  return path.join(app.getPath("userData"), "settings.json");
}

async function loadSettings() {
  try {
    const raw = await fs.readFile(settingsPath(), "utf8");
    return { ...DEFAULT_SETTINGS, ...JSON.parse(raw) };
  } catch {
    return DEFAULT_SETTINGS;
  }
}

async function saveSettings(settings) {
  const clean = { ...DEFAULT_SETTINGS, ...settings };
  await fs.mkdir(path.dirname(settingsPath()), { recursive: true });
  await fs.writeFile(settingsPath(), JSON.stringify(clean, null, 2), "utf8");
  return clean;
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: 1100,
    height: 760,
    minWidth: 900,
    minHeight: 640,
    title: "MinerU一键识别",
    backgroundColor: "#f7f7f2",
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  mainWindow.loadFile(path.join(__dirname, "index.html"));
}

app.whenReady().then(createWindow);
app.whenReady().then(() => Menu.setApplicationMenu(null));
app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});
app.on("activate", () => {
  if (BrowserWindow.getAllWindows().length === 0) createWindow();
});

ipcMain.handle("settings:load", loadSettings);
ipcMain.handle("settings:save", (_event, settings) => saveSettings(settings));

ipcMain.handle("dialog:choose-output", async () => {
  const result = await dialog.showOpenDialog(mainWindow, {
    title: "选择默认项目目录",
    properties: ["openDirectory", "createDirectory"]
  });
  return result.canceled ? null : result.filePaths[0];
});

ipcMain.handle("shell:open-path", async (_event, targetPath) => {
  if (!targetPath) return;
  await shell.openPath(targetPath);
});

ipcMain.handle("jobs:start", async (_event, filePaths, settings) => {
  if (isRunning) {
    throw new Error("已有识别任务正在运行，请等待当前队列完成。");
  }
  isRunning = true;
  const finalSettings = { ...DEFAULT_SETTINGS, ...settings };
  try {
    if (!finalSettings.exportMarkdown && !finalSettings.exportWord) {
      throw new Error("请至少开启 Markdown 或 Word 导出。");
    }
    for (const filePath of filePaths) {
      await processFile(filePath, finalSettings);
    }
  } finally {
    isRunning = false;
    send("queue:done", {});
  }
});

function send(channel, payload) {
  if (!mainWindow || mainWindow.isDestroyed()) return;
  mainWindow.webContents.send(channel, payload);
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function safeName(input) {
  return input
    .replace(/[<>:"/\\|?*\u0000-\u001f]/g, "_")
    .replace(/\s+/g, " ")
    .trim()
    .slice(0, 120) || "未命名文件";
}

function timestamp() {
  const d = new Date();
  const pad = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}_${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`;
}

async function fileHash(filePath) {
  const buffer = await fs.readFile(filePath);
  return crypto.createHash("sha256").update(buffer).digest("hex");
}

async function appendLog(projectDir, line) {
  await fs.appendFile(path.join(projectDir, "run-log.txt"), `[${new Date().toISOString()}] ${line}\n`, "utf8");
}

async function requestJson(url, options) {
  const response = await fetch(url, options);
  const text = await response.text();
  let body;
  try {
    body = text ? JSON.parse(text) : {};
  } catch {
    body = { raw: text };
  }
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}: ${body.msg || body.raw || response.statusText}`);
  }
  if (body.code !== undefined && body.code !== 0) {
    throw new Error(body.msg || `MinerU 返回错误 code=${body.code}`);
  }
  return body;
}

async function processFile(filePath, settings) {
  const fileName = path.basename(filePath);
  const baseName = path.basename(fileName, path.extname(fileName));
  const id = crypto.randomUUID();
  const jobPayload = { id, fileName };

  let projectDir = "";
  try {
    if (!settings.token) throw new Error("请先在设置里填写 MinerU Token。");

    const stat = await fs.stat(filePath);
    if (stat.size > 200 * 1024 * 1024) {
      throw new Error("文件超过 MinerU 精准解析 API 的 200MB 限制。");
    }

    projectDir = path.join(settings.outputRoot, `${timestamp()}_${safeName(baseName)}`);
    await fs.mkdir(projectDir, { recursive: true });
    await fs.copyFile(filePath, path.join(projectDir, fileName));

    const hash = await fileHash(filePath);
    await appendLog(projectDir, `开始识别：${filePath}`);
    await appendLog(projectDir, `SHA256：${hash}`);
    send("job:update", { ...jobPayload, state: "upload-url", text: "申请上传地址", projectDir });

    const dataId = `${safeName(baseName)}_${Date.now()}`.replace(/[^\w.-]/g, "_").slice(0, 128);
    const createBody = {
      files: [{ name: fileName, data_id: dataId, is_ocr: Boolean(settings.isOcr) }],
      model_version: settings.modelVersion,
      language: settings.language,
      enable_table: Boolean(settings.enableTable),
      enable_formula: Boolean(settings.enableFormula)
    };
    if (settings.exportWord) createBody.extra_formats = ["docx"];

    const createResult = await requestJson(`${API_BASE}/api/v4/file-urls/batch`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${settings.token}`
      },
      body: JSON.stringify(createBody)
    });

    await fs.writeFile(path.join(projectDir, "mineru-create-response.json"), JSON.stringify(createResult, null, 2), "utf8");

    const batchId = createResult.data?.batch_id;
    const uploadUrl = normalizeUploadUrl(createResult.data?.file_urls?.[0]);
    if (!batchId || !uploadUrl) {
      throw new Error("MinerU 没有返回 batch_id 或上传地址。");
    }

    await appendLog(projectDir, `batch_id：${batchId}`);
    send("job:update", { ...jobPayload, state: "uploading", text: "上传文件", batchId, projectDir });

    const fileBuffer = await fs.readFile(filePath);
    const uploadResponse = await fetch(uploadUrl, {
      method: "PUT",
      body: fileBuffer
    });
    if (!uploadResponse.ok) {
      throw new Error(`文件上传失败：HTTP ${uploadResponse.status}`);
    }

    send("job:update", { ...jobPayload, state: "polling", text: "等待 MinerU 识别", batchId, projectDir });
    const result = await waitForBatch(batchId, settings, jobPayload, projectDir);

    send("job:update", { ...jobPayload, state: "downloading", text: "下载结果", batchId, projectDir });
    const zipUrl = pickResultUrl(result, ["full_zip_url", "zip_url", "result_zip_url"]);
    if (!zipUrl) throw new Error("任务完成但没有返回结果 zip 地址。");

    const zipResponse = await fetch(zipUrl);
    if (!zipResponse.ok) throw new Error(`结果包下载失败：HTTP ${zipResponse.status}`);
    const zipBuffer = Buffer.from(await zipResponse.arrayBuffer());
    const zipPath = path.join(projectDir, "mineru-result.zip");
    await fs.writeFile(zipPath, zipBuffer);

    const exports = await extractSelectedExports(zipBuffer, projectDir, settings);
    const sourceOutputPaths = [];
    for (const exported of exports) {
      const sourceOutputPath = await copyExportBesideSource(exported.archivePath, filePath, exported.extension);
      sourceOutputPaths.push(sourceOutputPath);
      await appendLog(projectDir, `归档 ${exported.label}：${exported.archivePath}`);
      await appendLog(projectDir, `复制到原目录：${sourceOutputPath}`);
    }
    send("job:update", {
      ...jobPayload,
      state: "done",
      text: `完成（${exports.map((exported) => exported.label).join(" / ")}）`,
      batchId,
      projectDir,
      sourceOutputPaths,
      sourceDir: path.dirname(filePath)
    });
  } catch (error) {
    if (projectDir) {
      await appendLog(projectDir, `失败：${error.message}`).catch(() => {});
    }
    send("job:update", {
      ...jobPayload,
      state: "failed",
      text: error.message,
      projectDir
    });
  }
}

async function waitForBatch(batchId, settings, jobPayload, projectDir) {
  const deadline = Date.now() + Number(settings.timeoutMinutes || 60) * 60 * 1000;
  let lastState = "";
  while (Date.now() < deadline) {
    const result = await requestJson(`${API_BASE}/api/v4/extract-results/batch/${batchId}`, {
      method: "GET",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${settings.token}`
      }
    });
    await fs.writeFile(path.join(projectDir, "mineru-last-result.json"), JSON.stringify(result, null, 2), "utf8");

    const extractResult = Array.isArray(result.data?.extract_result)
      ? result.data.extract_result[0]
      : result.data?.extract_result;
    const state = extractResult?.state || result.data?.state || "unknown";

    if (state !== lastState) {
      lastState = state;
      await appendLog(projectDir, `任务状态：${state}`);
    }
    send("job:update", {
      ...jobPayload,
      state: "polling",
      text: statusText(state),
      batchId,
      projectDir
    });

    if (state === "done") return extractResult;
    if (state === "failed") throw new Error(extractResult?.err_msg || "MinerU 识别失败。");

    await sleep(Number(settings.pollIntervalSeconds || 10) * 1000);
  }
  throw new Error("等待超时，请稍后用 batch_id 到 MinerU 后台查询。");
}

function normalizeUploadUrl(uploadEntry) {
  if (typeof uploadEntry === "string") return uploadEntry;
  if (!uploadEntry || typeof uploadEntry !== "object") return "";
  return uploadEntry.url || uploadEntry.upload_url || uploadEntry.file_url || "";
}

function pickResultUrl(result, keys) {
  for (const key of keys) {
    if (typeof result?.[key] === "string" && result[key]) return result[key];
  }
  for (const value of Object.values(result || {})) {
    if (typeof value === "string" && /^https?:\/\//i.test(value) && /\.zip(\?|$)/i.test(value)) {
      return value;
    }
  }
  return "";
}

function statusText(state) {
  const labels = {
    "waiting-file": "等待文件上传",
    pending: "排队中",
    running: "识别中",
    converting: "转换结果中",
    uploading: "上传中"
  };
  return labels[state] || `处理中：${state}`;
}

async function extractSelectedExports(zipBuffer, projectDir, settings) {
  const zip = await JSZip.loadAsync(zipBuffer);
  const entries = Object.values(zip.files).filter((file) => !file.dir);
  const selected = [];
  if (settings.exportMarkdown) {
    const entry = entries.find((file) => /(^|\/)full\.md$/i.test(file.name));
    if (!entry) throw new Error("结果包里没有找到 full.md，原始结果包已归档。");
    selected.push({ entry, label: "MD", extension: ".md", archiveName: "full.md" });
  }
  if (settings.exportWord) {
    const entry = entries.find((file) => /(^|\/)full\.docx$/i.test(file.name))
      || entries.find((file) => /\.docx$/i.test(file.name));
    if (!entry) throw new Error("MinerU 结果包里没有 Word 文件，原始结果包已归档；可关闭 Word 导出后重新识别。");
    selected.push({ entry, label: "Word", extension: ".docx", archiveName: "full.docx" });
  }
  for (const exported of selected) {
    const content = await exported.entry.async("nodebuffer");
    exported.archivePath = await saveWithoutOverwrite(path.join(projectDir, exported.archiveName),
      (targetPath) => fs.writeFile(targetPath, content, { flag: "wx" }));
  }
  return selected;
}

async function copyExportBesideSource(archivePath, sourceFilePath, extension) {
  const sourceDir = path.dirname(sourceFilePath);
  const sourceBaseName = path.basename(sourceFilePath, path.extname(sourceFilePath));
  const preferredPath = path.join(sourceDir, `${sourceBaseName}${extension}`);
  return saveWithoutOverwrite(preferredPath,
    (targetPath) => fs.copyFile(archivePath, targetPath, fs.constants.COPYFILE_EXCL));
}

async function saveWithoutOverwrite(preferredPath, writeFile) {
  const { dir, name, ext } = path.parse(preferredPath);
  for (let index = 0; ; index += 1) {
    const suffix = index === 0 ? "" : `_识别结果${index === 1 ? "" : `_${index}`}`;
    const targetPath = path.join(dir, `${name}${suffix}${ext}`);
    try {
      await writeFile(targetPath);
      return targetPath;
    } catch (error) {
      if (error.code !== "EEXIST") throw error;
    }
  }
}
