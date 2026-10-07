const electron = require("electron");
const { contextBridge, ipcRenderer } = electron;
const webUtils = electron.webUtils;

contextBridge.exposeInMainWorld("mineruApp", {
  loadSettings: () => ipcRenderer.invoke("settings:load"),
  saveSettings: (settings) => ipcRenderer.invoke("settings:save", settings),
  chooseOutput: () => ipcRenderer.invoke("dialog:choose-output"),
  startJobs: (filePaths, settings) => ipcRenderer.invoke("jobs:start", filePaths, settings),
  openPath: (targetPath) => ipcRenderer.invoke("shell:open-path", targetPath),
  getPathForFile: (file) => {
    if (webUtils?.getPathForFile) return webUtils.getPathForFile(file);
    return file?.path || "";
  },
  onJobUpdate: (callback) => ipcRenderer.on("job:update", (_event, payload) => callback(payload)),
  onQueueDone: (callback) => ipcRenderer.on("queue:done", (_event, payload) => callback(payload))
});
