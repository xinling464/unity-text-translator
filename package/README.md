# 团子冲击！

作者 **YG（@xinling464）** · [官方仓库](https://github.com/xinling464/unity-text-translator)

在 Unity 编辑器里将英文和日文界面文字显示成简体中文。适用于 Unity 2022.3 的 VRChat 制作工作流，包含 Modular Avatar、lilToon、VRChat SDK 等工具常见术语。

包 ID 为 com.yg.unity-text-translator。

## 安装

- 使用 .unitypackage 时，在 Unity 里选择 Assets → Import Package → Custom Package，或将文件拖入 Project 窗口。项目里只保留一种安装方式。
- ALCOM / VCC 可以添加 [主仓库索引](https://raw.githubusercontent.com/xinling464/unity-text-translator/main/index.json)，然后在项目的包管理里安装「团子冲击！」。
- 离线安装时，把对应 ZIP 的内容解压到项目的 Packages/com.yg.unity-text-translator/，确认该目录里直接有 package.json。已有旧版本时，用同一种安装方式升级。

## 使用

1. 打开 Unity 顶部「团子冲击！」菜单。在设置里按项目开启或暂停界面翻译，也可以按插件停用。
2. 离线词表默认可用。「显示原文」可用 Ctrl/Cmd + Shift + Y 切换，方便跟着原文教程操作。
3. 在「术语」页查看待校对文字，填写原文和译文、标记忽略、导入或导出 JSON。包专用词条优先于通用词条。
4. 如需机器翻译，在设置中开启在线翻译，选择服务并用「测试连接」试译固定样例。服务接收待译的界面文字。关掉在线请求后仍可使用离线词表和此前缓存。
5. 「中英命令搜索」支持按中文、英文或原菜单路径搜索，调用已验证的 Unity 原菜单。

## 原生界面别名

- 层级、项目树／列表／网格和 BlendShape 滑块可显示中文别名，并可单独关闭。悬停或使用「显示原文」查看真实名称。
- 「中文定位」支持搜索、选中、定位对象和资源，亦可保存单项别名。已保存的场景对象、资源和网格可以持久保存别名。
- Unity 2022.3.22f1 为原生适配的首个支持版本。缺少适配入口时该处用原文。项目名称和形态键名称留在本地。
- UnityAgent 0.17.0 安装后，使用「UnityAgent → 中文」运行原命令。Avatar Optimizer 和 Texture Atlas 还要求原插件对应的依赖。

## 应用和还原目录补丁

「目录补丁」页可先检查每个支持文件的原文和改动，选择要应用的文件。初次打开工程或发现新包时只扫描。包自带词典已有的中文优先保留。

「还原」基于变更记录和文件哈希。文件若在汉化后被用户或包更新改过，面板会列出冲突并保留当前文件。旧版 .ygzh.bak 可在内容匹配时还原。

## 覆盖范围

插件处理已识别的编辑器展示文本，输入框内容、对象名、资源路径和项目数据保持原样。部分第三方控件使用自绘或把显示文本兼作程序标识。遇到暂未覆盖的界面，可优先使用插件自带中文、术语词表或命令搜索。

## 从源码打包

使用 PowerShell 7 运行 tools/build-release.ps1。脚本以 package/ 为源，生成版本 ZIP，计算 SHA-256 并更新主仓库与镜像索引。

## 使用授权

个人非商业使用免费。商业使用、转发安装包或改包再发布需先取得作者许可。欢迎分享官方链接。详见[使用授权](LICENSE.md)与[第三方许可](THIRD%20PARTY%20NOTICES.md)。
