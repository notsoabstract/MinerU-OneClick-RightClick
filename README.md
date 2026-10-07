# MinerU 一键识别与独立右键识别

本仓库包含两套 Windows 工具：完整源码、启动文件、必要安装文件及本地测试源码。右键工具附带已经编译好的程序，可以直接安装。

## 一键识别窗口：one-click

1. 先安装 Node.js，然后双击 `one-click/安装依赖.bat` 恢复依赖。
2. 双击 `one-click/启动MinerU一键识别-无黑窗.vbs` 启动窗口。
3. 在窗口的设置区域填写自己的 MinerU Token，选择 Markdown、Word 或同时导出。
4. 将文件拖进窗口，开始识别。可用 `one-click/创建桌面快捷方式.vbs` 创建快捷方式。

源码和无黑窗启动器都已包含。Git 包不含 `node_modules`，依赖通过安装脚本及 `package-lock.json` 恢复。详细说明见 `one-click/README.md`。

## 独立右键工具：right-click

1. 在 Windows 11 x64 上双击 `right-click/dist/安装右键菜单.vbs`。
2. 首次安装会弹出一次 Windows 管理员确认，用于登记本工具的公开安装签名；日常识别不需要管理员权限。
3. 双击 `right-click/dist/识别设置.vbs`，填自己的 MinerU Token，并选择 MD、Word 或两者。
4. 右键单个支持的文件，点击“用 MinerU 识别”。

右键识别由自己的 `MinerURightClick.exe` 执行，不依赖一键识别窗口、Electron 或 Node.js。设置也可以从开始菜单“MinerU右键识别 → 识别设置”打开，或者从识别进度窗口的设置按钮进入。

安装后程序保存在 `%LOCALAPPDATA%\MinerURightClick\app`，Token 与导出开关保存在 `%LOCALAPPDATA%\MinerURightClick\settings.json`，Token 按当前 Windows 账号加密保存。请通过设置窗口修改，不要手动编辑加密字段。

两套工具的 API 设置互不自动同步。换 Token 时分别修改两边；本仓库及压缩包不会提供作者个人 Token。详细使用说明见 `right-click/dist/使用说明.md`，开发说明见 `right-click/开发说明.md`。

## 上传 Git

解压后将整个本目录作为一个仓库上传，保留两个子目录。根目录 `.gitignore` 已排除常见个人配置、任务缓存、日志、识别文件、依赖、签名私钥及开发产物；右键工具的发布目录是刻意保留的，包含安装所需的 EXE、DLL、MSIX 和公开 CER 证书。

本包没有复制个人配置、识别归档、云端测试记录、旧备份或任何签名私钥。源文件中的默认 Token 为空，测试中的 Token 是虚构测试值。

## 2026-10-07 更新

移除右键菜单前增加确认提示，默认选择“否”。取消或关闭提示不会移除入口，也不会显示卸载成功。VBS、开始菜单和直接运行卸载脚本均经过同一确认步骤。用本包的 `right-click/dist/安装右键菜单.vbs` 重新安装即可更新已有版本并恢复已移除的入口；已有 API 与导出设置会保留。
