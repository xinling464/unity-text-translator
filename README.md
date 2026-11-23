# 团子冲击！

> 把 Unity 编辑器里的**英文 / 日文界面文字**显示成中文。能翻的都翻，不破坏插件功能，改前自动备份，随时一键还原。

包 id：`com.yg.unity-text-translator`（id 不变，改名不影响已安装的项目）

---

## 这是干什么的

你在 Unity 里装了一堆第三方插件（lilToon、Modular Avatar、Poiyomi、VRChat SDK、FaceEmo…），界面上一半英文一半日文，看不懂只能猜。

装了这个包之后：

- 插件 Inspector 里的标签（`Left Eye Bone` → **左眼骨**）
- 插件的窗口、按钮、提示
- 右键菜单（`Setup Outfit` → **设置服装 (Setup Outfit)**）
- 顶部菜单栏里插件注册的项
- 连 Unity 自己没翻干净的残留英文（`Face Tracking` → **面部追踪**）
- 以后**新导入的插件**同样自动翻

第一次看到还是英文不要紧，停一两秒会变中文（异步译文回来后重绘），之后就永久记住。

---

## 五类文字，四种处理

硬翻会坏功能，所以分层处理：

| 文字类型 | 例子 | 怎么处理 | 风险 |
|---|---|---|---|
| **运行时画的** | lilToon 面板、自绘 Inspector、`GUILayout.Label("...")` | Harmony 在 `GUI.Label` / `GUI.Box` / `GUILayout.Label` 绘制前换文本（8 个入口） | 零写入 |
| **EditorGUI 画的** | `Vector3Field("View Position")`、`PrefixLabel` 这类复合控件 | patch `EditorGUI.LabelField` / `PrefixLabel` 共 8 个重载 | 零写入 |
| **UI Toolkit 画的** | Modular Avatar、NDMF 的 `.uxml` 面板 | patch `TextElement.text` 的 setter，赋值前换文本 | 零写入 |
| **编译期定死的菜单** | `[MenuItem("Tools/...")]` | 改源码字符串成 `中文 (English)`，改前留 `.ygzh.bak` | 有写入，可还原 |
| **包自带词典** | MA 的 `Localization/zh-Hans.json` | 只补「英文有、中文缺」的条 | 有写入，有备份 |

合计 **16 个运行时 patch 点** + 菜单/词典的磁盘层。

**故意不翻**（翻了会断引用）：物体名、资源名、文件路径、Shader 属性名、枚举内部值（`Viseme: sil`、`VRC.v_xx`）。

---

## 怎么用

安装后编辑器顶部菜单栏会多一个 **团子冲击！**：

| 菜单项 | 作用 |
|---|---|
| **刷新翻译** | 点一次：确保已启用 → 清内存缓存 → 重绘所有面板 → 没翻的词条重新排队 |
| **暂停翻译** | 停止翻译，再点刷新即恢复 |
| 打开设置面板… | 开关、翻译后端、缓存统计、一键全翻译本项目插件、一键还原 |
| 打开数据目录 | `%APPDATA%\YGUnityTranslator`（词典、缓存、待译清单都在） |

翻译后端默认 **MyMemory**（免费、不用 key），也可换成有道 / DeepL / 百度 / 任意 OpenAI 兼容接口。

---

## 安装

### 走 ALCOM / VCC（推荐）

1. ALCOM → `Settings` → `Add Repository`，填：
   ```
   https://raw.githubusercontent.com/xinling464/unity-text-translator/main/index.json
   ```
2. 打开项目 → `Manage Packages` → 勾上 **团子冲击！** → 应用

### 手动

把整个包目录放进项目的 `Packages/com.yg.unity-text-translator/`（走 Packages 内嵌，不写 manifest 依赖）。

---

## 依赖与数据

- Unity 2022.3+（Editor 程序集，不参与打包）
- 零第三方依赖：Harmony 优先复用 NDMF 自带的 `0Harmony`，没有才用包内 `Dependencies~/0Harmony.dll`，**绝不加载第二份**
- 数据都在 `%APPDATA%\YGUnityTranslator\`：`lexicon.json`（自建词条）、`cache.json`（在线译文缓存）、`unseen.jsonl`（待译清单）、`packages.txt`（包快照）、`catalog-patch.log`（磁盘层日志）

## 还原

- 菜单/词典改动：设置面板「一键还原」，或删掉所有 `.ygzh.bak` 之外的手改
- 运行时替换：面板里「停用」立即摘掉全部 Harmony patch
- 彻底移除：ALCOM 取消勾选，或删 `Packages/com.yg.unity-text-translator`

## 已知限制

- 顶部菜单栏只能靠改 `[MenuItem]` 源码生效（原生 C++ 菜单，hook 拦不到）；**插件包升级会覆盖，需要重跑一次**
- 免费在线翻译接口有额度限制，大批量词条靠人工词典兜底
- 极少数把文字画在 Canvas / 自定义 mesh 上的面板，hook 覆盖不到
- 注释层（改源码里的注释）默认关闭，需要手动触发
