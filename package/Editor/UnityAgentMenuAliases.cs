#if YGT_UNITYAGENT_0_17_0
using UnityEditor;

namespace YGTextTranslator
{
    internal static class UnityAgentMenuAliases
    {
        const string OriginalRoot = "UnityAgent/";
        const string ChineseRoot = OriginalRoot + "中文/";

        const string OpenChatPath = OriginalRoot + "Open Chat";
        const string OpenChatLabel = "打开聊天";
        const string McpSettingsPath = OriginalRoot + "MCP Settings";
        const string McpSettingsLabel = "MCP 设置";
#if YGT_AVATAR_OPTIMIZER
        const string AvatarOptimizerPath = OriginalRoot + "Avatar Optimizer";
        const string AvatarOptimizerLabel = "Avatar 优化器";
#endif
        const string OutfitFittingPath = OriginalRoot + "Outfit Fitting";
        const string OutfitFittingLabel = "服装适配";
        const string MochiFitterCatalogPath = OriginalRoot + "MochiFitter Catalog";
        const string MochiFitterCatalogLabel = "MochiFitter 目录";
#if YGT_AVATAR_OPTIMIZER
        const string TextureAtlasPath = OriginalRoot + "Texture Atlas";
        const string TextureAtlasLabel = "纹理图集";
#endif
        const string MeshPainterPath = OriginalRoot + "Mesh Painter";
        const string MeshPainterLabel = "网格绘制";
        const string MeshWeightEditorPath = OriginalRoot + "Mesh and Weight Editor";
        const string MeshWeightEditorLabel = "网格与权重编辑器";
        const string ShrinkEditorPath = OriginalRoot + "Shrink Editor";
        const string ShrinkEditorLabel = "收缩形变编辑器";
        const string BonePoseEditorPath = OriginalRoot + "Bone Pose Editor";
        const string BonePoseEditorLabel = "骨骼姿势编辑器";
        const string PoseEstimationPath = OriginalRoot + "Pose Estimation";
        const string PoseEstimationLabel = "姿态估计";
        const string SkillManagementPath = OriginalRoot + "Skill Management";
        const string SkillManagementLabel = "技能管理";
        const string ToolConsolePath = OriginalRoot + "Tool Console";
        const string ToolConsoleLabel = "工具控制台";
        const string TranslationManagementPath = OriginalRoot + "Translation Management";
        const string TranslationManagementLabel = "翻译管理";
        const string FlowchartPath = OriginalRoot + "Flowchart";
        const string FlowchartLabel = "流程图";
        const string AgentLogPath = OriginalRoot + "Agent Log";
        const string AgentLogLabel = "智能体日志";

        internal static readonly (string Label, string SourcePath)[] Commands =
        {
            (OpenChatLabel, OpenChatPath),
            (McpSettingsLabel, McpSettingsPath),
#if YGT_AVATAR_OPTIMIZER
            (AvatarOptimizerLabel, AvatarOptimizerPath),
#endif
            (OutfitFittingLabel, OutfitFittingPath),
            (MochiFitterCatalogLabel, MochiFitterCatalogPath),
#if YGT_AVATAR_OPTIMIZER
            (TextureAtlasLabel, TextureAtlasPath),
#endif
            (MeshPainterLabel, MeshPainterPath),
            (MeshWeightEditorLabel, MeshWeightEditorPath),
            (ShrinkEditorLabel, ShrinkEditorPath),
            (BonePoseEditorLabel, BonePoseEditorPath),
            (PoseEstimationLabel, PoseEstimationPath),
            (SkillManagementLabel, SkillManagementPath),
            (ToolConsoleLabel, ToolConsolePath),
            (TranslationManagementLabel, TranslationManagementPath),
            (FlowchartLabel, FlowchartPath),
            (AgentLogLabel, AgentLogPath),
        };

        static void Execute(string originalPath) => EditorApplication.ExecuteMenuItem(originalPath);

        static bool Validate(string originalPath, string label)
        {
            var aliasPath = ChineseRoot + label;
            var isChecked = Menu.GetChecked(originalPath);
            if (Menu.GetChecked(aliasPath) != isChecked) Menu.SetChecked(aliasPath, isChecked);
            return Menu.GetEnabled(originalPath);
        }

        [MenuItem(ChineseRoot + OpenChatLabel, false, 500)]
        static void RunOpenChat() => Execute(OpenChatPath);

        [MenuItem(ChineseRoot + OpenChatLabel, true)]
        static bool ValidateOpenChat() => Validate(OpenChatPath, OpenChatLabel);

        [MenuItem(ChineseRoot + McpSettingsLabel, false, 501)]
        static void RunMcpSettings() => Execute(McpSettingsPath);

        [MenuItem(ChineseRoot + McpSettingsLabel, true)]
        static bool ValidateMcpSettings() => Validate(McpSettingsPath, McpSettingsLabel);

#if YGT_AVATAR_OPTIMIZER
        [MenuItem(ChineseRoot + AvatarOptimizerLabel, false, 600)]
        static void RunAvatarOptimizer() => Execute(AvatarOptimizerPath);

        [MenuItem(ChineseRoot + AvatarOptimizerLabel, true)]
        static bool ValidateAvatarOptimizer() => Validate(AvatarOptimizerPath, AvatarOptimizerLabel);
#endif

        [MenuItem(ChineseRoot + OutfitFittingLabel, false, 601)]
        static void RunOutfitFitting() => Execute(OutfitFittingPath);

        [MenuItem(ChineseRoot + OutfitFittingLabel, true)]
        static bool ValidateOutfitFitting() => Validate(OutfitFittingPath, OutfitFittingLabel);

        [MenuItem(ChineseRoot + MochiFitterCatalogLabel, false, 602)]
        static void RunMochiFitterCatalog() => Execute(MochiFitterCatalogPath);

        [MenuItem(ChineseRoot + MochiFitterCatalogLabel, true)]
        static bool ValidateMochiFitterCatalog() => Validate(MochiFitterCatalogPath, MochiFitterCatalogLabel);

#if YGT_AVATAR_OPTIMIZER
        [MenuItem(ChineseRoot + TextureAtlasLabel, false, 603)]
        static void RunTextureAtlas() => Execute(TextureAtlasPath);

        [MenuItem(ChineseRoot + TextureAtlasLabel, true)]
        static bool ValidateTextureAtlas() => Validate(TextureAtlasPath, TextureAtlasLabel);
#endif

        [MenuItem(ChineseRoot + MeshPainterLabel, false, 700)]
        static void RunMeshPainter() => Execute(MeshPainterPath);

        [MenuItem(ChineseRoot + MeshPainterLabel, true)]
        static bool ValidateMeshPainter() => Validate(MeshPainterPath, MeshPainterLabel);

        [MenuItem(ChineseRoot + MeshWeightEditorLabel, false, 701)]
        static void RunMeshWeightEditor() => Execute(MeshWeightEditorPath);

        [MenuItem(ChineseRoot + MeshWeightEditorLabel, true)]
        static bool ValidateMeshWeightEditor() => Validate(MeshWeightEditorPath, MeshWeightEditorLabel);

        [MenuItem(ChineseRoot + ShrinkEditorLabel, false, 702)]
        static void RunShrinkEditor() => Execute(ShrinkEditorPath);

        [MenuItem(ChineseRoot + ShrinkEditorLabel, true)]
        static bool ValidateShrinkEditor() => Validate(ShrinkEditorPath, ShrinkEditorLabel);

        [MenuItem(ChineseRoot + BonePoseEditorLabel, false, 703)]
        static void RunBonePoseEditor() => Execute(BonePoseEditorPath);

        [MenuItem(ChineseRoot + BonePoseEditorLabel, true)]
        static bool ValidateBonePoseEditor() => Validate(BonePoseEditorPath, BonePoseEditorLabel);

        [MenuItem(ChineseRoot + PoseEstimationLabel, false, 704)]
        static void RunPoseEstimation() => Execute(PoseEstimationPath);

        [MenuItem(ChineseRoot + PoseEstimationLabel, true)]
        static bool ValidatePoseEstimation() => Validate(PoseEstimationPath, PoseEstimationLabel);

        [MenuItem(ChineseRoot + SkillManagementLabel, false, 800)]
        static void RunSkillManagement() => Execute(SkillManagementPath);

        [MenuItem(ChineseRoot + SkillManagementLabel, true)]
        static bool ValidateSkillManagement() => Validate(SkillManagementPath, SkillManagementLabel);

        [MenuItem(ChineseRoot + ToolConsoleLabel, false, 801)]
        static void RunToolConsole() => Execute(ToolConsolePath);

        [MenuItem(ChineseRoot + ToolConsoleLabel, true)]
        static bool ValidateToolConsole() => Validate(ToolConsolePath, ToolConsoleLabel);

        [MenuItem(ChineseRoot + TranslationManagementLabel, false, 802)]
        static void RunTranslationManagement() => Execute(TranslationManagementPath);

        [MenuItem(ChineseRoot + TranslationManagementLabel, true)]
        static bool ValidateTranslationManagement() => Validate(TranslationManagementPath, TranslationManagementLabel);

        [MenuItem(ChineseRoot + FlowchartLabel, false, 803)]
        static void RunFlowchart() => Execute(FlowchartPath);

        [MenuItem(ChineseRoot + FlowchartLabel, true)]
        static bool ValidateFlowchart() => Validate(FlowchartPath, FlowchartLabel);

        [MenuItem(ChineseRoot + AgentLogLabel, false, 804)]
        static void RunAgentLog() => Execute(AgentLogPath);

        [MenuItem(ChineseRoot + AgentLogLabel, true)]
        static bool ValidateAgentLog() => Validate(AgentLogPath, AgentLogLabel);
    }
}
#endif
