# 个人配置与签名材料

发布包不携带个人 MinerU Token。API 默认值为空，其他使用者需要填写自己的 Token。

一键窗口的个人设置在 `%APPDATA%\mineru-one-click\settings.json`；右键工具的个人设置在 `%LOCALAPPDATA%\MinerURightClick\settings.json`。两处配置都不要复制到 Git 仓库，即使右键 Token 已加密也不应上传。

识别文档、结果、日志、任务缓存、历史备份、`.env`、签名 PFX 和签名密码均不属于发布内容。根 `.gitignore` 对常见路径提供防误提交规则。

`right-click/dist/MinerURightClick.cer` 是右键安装需要的公开证书，可以随包提供；它不含 API，也不含签名私钥。
