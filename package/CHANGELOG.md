## 0.2.1

- 设置窗口显示作者 YG、包版本和官方仓库入口。
- 两种安装包都附带使用授权和第三方许可。

## 0.2.0

- 在 Unity 2022.3.22f1 的层级、项目树／列表／网格和 BlendShape 检查器显示离线中文别名。
- 支持按界面关闭别名、悬停查看原名和使用原文切换快捷键。
- 在「中文定位」中搜索已加载场景对象与 Assets 下的资源，选中原对象或编辑术语别名。
- 对 UnityAgent 0.17.0 增加「UnityAgent → 中文」入口，并收录到中英命令搜索。
- 支持直接导入的 .unitypackage 和 VPM 安装包。

## 0.1.9

- UI Toolkit 译文只进入布局和绘制链路，暂停和恢复时同步刷新。输入框、绑定值和运行时 UI 保持原样。
- IMGUI 继续使用 Unity 原控件的方法和交互逻辑。可暂停、临时显示原文、按包关闭翻译。
- 新增术语校对、词典导入导出、中英命令搜索和 263 条离线词条。
- 在线请求按项目开启，关掉后仍可读取译文缓存。支持连接测试、重试、提供方配置隔离与可选模型。
- 扫描新包只提示。词典和菜单文件支持按项预览和应用，按补丁记录复原，保留改写之后的用户或包更新。
- 主索引和镜像索引由同一发布脚本更新。

## 0.1.6
- 修复：菜单双语化改成递归扫描包内所有 `.cs`（之前只扫包根，MA 的 `Runtime/UI/UnityMenuItems.cs` 扫不到，日志「菜单改 0 处」）
- 修复：设置面板顶部的版本号改成从 `package.json` 读取（之前硬编码 0.1.0）

## 0.1.5
- 新增：内置 143 条菜单词表（含 Modular Avatar 全部菜单），首次打开项目自动把第三方右键/顶部菜单改成「中文 (English)」
- 新增：每个项目只自动补一次（`%APPDATA%\YGUnityTranslator\menu-auto.txt` 记着），不会反复改文件
- 说明：插件包更新会覆盖菜单补丁，重装后在「团子冲击！ → 刷新翻译」里再补一次即可

## 0.1.4
- 新增：一键导入的 `.unitypackage`（双击导入即用，不用走 ALCOM / Package Manager）
- 新增：HarmonyLoader 支持 Assets 装法（用 `AssetDatabase.FindAssets` 找 0Harmony.dll）
- 修复：删掉 4 条在 Unity 2022.3 里不存在的 Foldout patch，启动不再刷红字

# 0.1.3

- 修「输入框消失」：`EditorGUI.PrefixLabel` 的返回类型是 `Rect`（label 画完后剩下的区域），之前命中词典就接管绘制并 `return false`，原方法不执行、返回值变成 0×0，导致 VRChat SDK 的密码框这类控件画不出来。现在这 4 个重载只改文本、恒 `return true`。
- 控件层替换入口全部加 `try/catch`：任何意外都退回原文，绝不让一处异常把后面的控件整片带走。

# 0.1.2

- 控件层补全：新增 `GUILayout.Toggle` / `Button` / `Foldout` 等 11 个绘制入口的 patch（HierarchyPlus 这类自绘控件面板之前完全没拦住）。合计 23 个 patch 点，日志会打 `PatchedCount`。
- 修掉「翻过头」：新增标识符过滤，物体名 `Body_Base`、资源名 `0Harmony.dll`、`xxx (Clone)`、无空格驼峰 `CostumeWing` 一律不翻，Hierarchy 不再显示奇怪的中文。
- 菜单汉化工具支持三种写法（`[MenuItem]` 字面量 / 完整路径常量 / 常量 + 片段拼接），VRCFury 那种 `utilities + "List All Components"` 也能改。

# 0.1.1

- 改名「团子冲击！」（包 id `com.yg.unity-text-translator` 不变，已装项目无需重装）。
- 顶部菜单新增「团子冲击！」入口：刷新翻译 / 暂停翻译 / 打开设置面板 / 打开数据目录。
- 首次启动自动启用拦截（之前需要手动跑自检才会开，属于严重缺陷）。
- 重写 README。

# 0.1.0

- 首个可用版本：运行时 IMGUI 文本替换（GUI.Label / GUI.Box / GUILayout.Label），中文字体自动挂微软雅黑。
- 在线翻译：MyMemory / 有道 / DeepL / 百度 / OpenAI 兼容，译文永久缓存，未命中异步补翻。
- 目录级汉化：补插件自带词典缺条、把写死英文的菜单改成「中文 (English)」，全部留 .ygzh.bak 可一键还原。
- 新插件入场自动扫描并按需汉化。
- 面板：在线翻译开关/后端选择/缓存统计/一键全翻译/一键还原。
