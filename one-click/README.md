# MinerU 一键识别

一个日常文档识别用的 MinerU 桌面入口，通过 MinerU 精准解析 API 识别文件。

## 功能

- 拖拽 PDF、Word、PPT、Excel（xls/xlsx）、图片到窗口
- 自动调用 MinerU `/api/v4/file-urls/batch`
- 自动上传、轮询任务结果、下载 zip
- Markdown（md）和 Word（docx）两个独立导出开关，可同时开启，至少开启一个
- 默认只导出 Markdown；开启 Word 时向 MinerU 请求 docx 导出
- 将选中的结果复制到原文件夹，并在队列中显示实际保存的文件名
- 已有文件不覆盖：例如 `合同.md` 已存在，则保存为 `合同_识别结果.md`；再次冲突则使用 `合同_识别结果_2.md`，依次递增
- Word 使用相同的保护规则，识别 Word 原文件时不会覆盖原文件
- 按文件建立项目文件夹，默认保存到 `D:\MinerU`
- 保存原始文件、结果包、选中的导出文件和运行日志；不单独解压图片

## 使用

1. 安装 Node.js。
2. 双击 `安装依赖.bat`。
3. 双击 `启动MinerU一键识别-无黑窗.vbs`（`start-silent.vbs` 也可以）。
4. 如果想放到桌面，双击 `create-desktop-shortcut.vbs`。

也可以在本目录手动运行：

```powershell
npm install
npm start
```

4. 打开设置，填入 MinerU Token。
5. 拖入文件，点击开始识别。

## 说明

结果保存在原文件夹，项目目录中保留完整归档。若 MinerU 返回的结果包缺少所选格式，程序会明确提示失败并保留结果包。Excel 输入使用现有云端接口，无需安装本地识别模型。
