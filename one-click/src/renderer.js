const queueEl = document.querySelector("#queue");
const dropZone = document.querySelector("#dropZone");
const fileInput = document.querySelector("#fileInput");
const pickFiles = document.querySelector("#pickFiles");
const startJobsButton = document.querySelector("#startJobs");
const clearQueueButton = document.querySelector("#clearQueue");
const toast = document.querySelector("#toast");

const controls = {
  token: document.querySelector("#token"),
  outputRoot: document.querySelector("#outputRoot"),
  modelVersion: document.querySelector("#modelVersion"),
  language: document.querySelector("#language"),
  isOcr: document.querySelector("#isOcr"),
  enableTable: document.querySelector("#enableTable"),
  enableFormula: document.querySelector("#enableFormula"),
  exportMarkdown: document.querySelector("#exportMarkdown"),
  exportWord: document.querySelector("#exportWord"),
  pollIntervalSeconds: document.querySelector("#pollIntervalSeconds"),
  timeoutMinutes: document.querySelector("#timeoutMinutes")
};

const files = new Map();
let running = false;

init();

async function init() {
  const settings = await window.mineruApp.loadSettings();
  applySettings(settings);
  renderQueue();

  document.querySelector("#saveSettings").addEventListener("click", saveSettings);
  document.querySelector("#chooseOutput").addEventListener("click", chooseOutput);
  pickFiles.addEventListener("click", () => fileInput.click());
  fileInput.addEventListener("change", () => addFiles([...fileInput.files]));
  clearQueueButton.addEventListener("click", clearQueue);
  startJobsButton.addEventListener("click", startJobs);

  dropZone.addEventListener("dragover", (event) => {
    event.preventDefault();
    dropZone.classList.add("dragging");
  });
  dropZone.addEventListener("dragleave", () => dropZone.classList.remove("dragging"));
  dropZone.addEventListener("drop", (event) => {
    event.preventDefault();
    dropZone.classList.remove("dragging");
    addFiles([...event.dataTransfer.files]);
  });

  window.mineruApp.onJobUpdate(updateJob);
  window.mineruApp.onQueueDone(() => {
    running = false;
    startJobsButton.disabled = false;
    startJobsButton.textContent = "开始识别";
    showToast("队列处理完成");
  });
}

function applySettings(settings) {
  controls.token.value = settings.token || "";
  controls.outputRoot.value = settings.outputRoot || "D:\\MinerU";
  controls.modelVersion.value = settings.modelVersion || "vlm";
  controls.language.value = settings.language || "ch";
  controls.isOcr.checked = Boolean(settings.isOcr);
  controls.enableTable.checked = Boolean(settings.enableTable);
  controls.enableFormula.checked = Boolean(settings.enableFormula);
  controls.exportMarkdown.checked = settings.exportMarkdown !== false;
  controls.exportWord.checked = Boolean(settings.exportWord);
  controls.pollIntervalSeconds.value = settings.pollIntervalSeconds || 10;
  controls.timeoutMinutes.value = settings.timeoutMinutes || 60;
}

function readSettings() {
  return {
    token: controls.token.value.trim(),
    outputRoot: controls.outputRoot.value.trim() || "D:\\MinerU",
    modelVersion: controls.modelVersion.value,
    language: controls.language.value,
    isOcr: controls.isOcr.checked,
    enableTable: controls.enableTable.checked,
    enableFormula: controls.enableFormula.checked,
    exportMarkdown: controls.exportMarkdown.checked,
    exportWord: controls.exportWord.checked,
    pollIntervalSeconds: Number(controls.pollIntervalSeconds.value || 10),
    timeoutMinutes: Number(controls.timeoutMinutes.value || 60)
  };
}

async function saveSettings() {
  await window.mineruApp.saveSettings(readSettings());
  showToast("设置已保存");
}

async function chooseOutput() {
  const selected = await window.mineruApp.chooseOutput();
  if (selected) {
    controls.outputRoot.value = selected;
    await saveSettings();
  }
}

function addFiles(fileList) {
  for (const file of fileList) {
    const filePath = window.mineruApp.getPathForFile(file);
    if (!filePath) continue;
    if (!isSupported(file.name)) {
      showToast(`暂不支持：${file.name}`);
      continue;
    }
    files.set(filePath, {
      path: filePath,
      name: file.name,
      size: file.size,
      state: "ready",
      text: "等待开始"
    });
  }
  renderQueue();
}

function isSupported(name) {
  return /\.(pdf|png|jpe?g|jp2|webp|gif|bmp|docx?|pptx?|xlsx?)$/i.test(name);
}

function renderQueue() {
  const items = [...files.values()];
  queueEl.classList.toggle("empty", items.length === 0);
  if (items.length === 0) {
    queueEl.innerHTML = '<div class="empty-state">还没有文件。</div>';
    return;
  }

  queueEl.innerHTML = items.map((item) => `
    <article class="job ${item.state}" data-path="${escapeHtml(item.path)}">
      <div class="job-main">
        <div class="file-name">${escapeHtml(item.name)}</div>
        <div class="file-path">${escapeHtml(item.path)}</div>
        ${item.sourceOutputPaths?.length ? `<div class="result-files">已保存：${item.sourceOutputPaths.map((targetPath) => escapeHtml(targetPath.split(/[\\/]/).pop())).join("、")}</div>` : ""}
      </div>
      <div class="job-meta">
        <span>${formatSize(item.size)}</span>
        <span class="state-label">${escapeHtml(item.text)}</span>
      </div>
      ${item.projectDir ? `<button class="open-folder" data-open="${escapeHtml(item.sourceDir || item.projectDir)}" type="button">打开目录</button>` : ""}
    </article>
  `).join("");

  queueEl.querySelectorAll("[data-open]").forEach((button) => {
    button.addEventListener("click", () => window.mineruApp.openPath(button.dataset.open));
  });
}

function clearQueue() {
  if (running) return;
  files.clear();
  renderQueue();
}

async function startJobs() {
  if (running) return;
  const settings = readSettings();
  if (!settings.exportMarkdown && !settings.exportWord) {
    showToast("请至少开启 Markdown 或 Word 导出");
    return;
  }
  if (!settings.token) {
    showToast("请先填写 MinerU Token");
    controls.token.focus();
    return;
  }
  const paths = [...files.values()]
    .filter((item) => item.state !== "done")
    .map((item) => item.path);
  if (paths.length === 0) {
    showToast("请先加入文件");
    return;
  }

  await window.mineruApp.saveSettings(settings);
  running = true;
  startJobsButton.disabled = true;
  startJobsButton.textContent = "识别中";
  try {
    await window.mineruApp.startJobs(paths, settings);
  } catch (error) {
    running = false;
    startJobsButton.disabled = false;
    startJobsButton.textContent = "开始识别";
    showToast(error.message || "启动失败");
  }
}

function updateJob(payload) {
  const item = [...files.values()].find((candidate) => candidate.name === payload.fileName);
  if (!item) return;
  item.state = payload.state;
  item.text = payload.text;
  item.projectDir = payload.projectDir;
  item.sourceDir = payload.sourceDir || item.sourceDir;
  item.batchId = payload.batchId;
  item.mdPath = payload.mdPath;
  item.sourceMdPath = payload.sourceMdPath;
  item.sourceOutputPaths = payload.sourceOutputPaths;
  renderQueue();
}

function showToast(message) {
  toast.textContent = message;
  toast.classList.add("show");
  window.clearTimeout(showToast.timer);
  showToast.timer = window.setTimeout(() => toast.classList.remove("show"), 2400);
}

function formatSize(size) {
  if (size < 1024 * 1024) return `${Math.max(1, Math.round(size / 1024))} KB`;
  return `${(size / 1024 / 1024).toFixed(1)} MB`;
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (char) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;"
  }[char]));
}
