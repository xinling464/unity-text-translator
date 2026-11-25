using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace YGTextTranslator
{
    internal static class NativeAliasTerms
    {
        static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
        static readonly Regex WordBreak = new Regex(
            @"(?<=[a-z])(?=[A-Z0-9])|(?<=[0-9])(?=[a-zA-Z])|(?<=[A-Z])(?=[A-Z][a-z])|[_.\s-]+",
            RegexOptions.CultureInvariant);
        static readonly Regex ShapeSuffix = new Regex(@"^(?<m>.+)_(?<direction>up|down|left|right|front|back|outside|center|on|off|big|small|only|white|slim|goat|star)(?<number>[0-9]*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex EyeVariant = new Regex(@"^eye_(?<variant>star|flower)(?<number>[0-9]*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Dictionary<string, string> Exact = new Dictionary<string, string>(Comparer)
        {
            ["Assets"] = "资源", ["Favorites"] = "收藏",
            ["Main Camera"] = "主摄像机", ["Directional Light"] = "方向光",
            ["armature"] = "骨架", ["Hips"] = "髋部", ["Spine"] = "脊柱", ["Chest"] = "胸部",
            ["UpperLeg"] = "大腿", ["LowerLeg"] = "小腿", ["UpperArm"] = "上臂", ["LowerArm"] = "前臂",
            ["Body_Base"] = "身体底模", ["Body Mesh"] = "身体网格", ["Jellyfish hair"] = "水母头发",
            ["Hair_Base"] = "头发底模", ["Cat_Ear"] = "猫耳", ["Cat_Tail"] = "猫尾",
            ["AvatarDynamics"] = "模型动态", ["Avatar Menu"] = "模型菜单", ["Editor Default Resources"] = "编辑器默认资源",
            ["Shaders"] = "着色器", ["BlendTree"] = "混合树", ["SubMenu"] = "子菜单",
            ["Face Tracking"] = "面部追踪", ["Action Menu"] = "动作菜单", ["Body Texture"] = "身体贴图",
            ["髪"] = "头发", ["前髪"] = "刘海", ["後ろ髪"] = "后发", ["横髪"] = "侧发",
            ["まつげ"] = "睫毛", ["睫毛"] = "睫毛", ["まぶた"] = "眼睑", ["瞳"] = "瞳孔",
            ["眉毛"] = "眉毛", ["衣装"] = "服装", ["しっぽ"] = "尾巴", ["表情メニュー"] = "表情菜单",
            ["テクスチャ"] = "贴图", ["マテリアル"] = "材质", ["アニメーション"] = "动画",
            ["プレハブ"] = "预制体", ["メッシュ"] = "网格", ["シェーダー"] = "着色器",
            ["メインカメラ"] = "主摄像机", ["パラメータ"] = "参数", ["オフ"] = "关闭", ["オン"] = "开启"
        };

        static readonly Dictionary<string, string> Words = new Dictionary<string, string>(Comparer)
        {
            ["main"] = "主", ["base"] = "底模", ["body"] = "身体", ["root"] = "根",
            ["bone"] = "骨骼", ["armature"] = "骨架", ["hips"] = "髋部", ["spine"] = "脊柱",
            ["chest"] = "胸部", ["neck"] = "颈部", ["head"] = "头部", ["arm"] = "手臂",
            ["hand"] = "手", ["finger"] = "手指", ["leg"] = "腿", ["foot"] = "脚",
            ["tail"] = "尾巴", ["hair"] = "头发", ["ear"] = "耳朵", ["cat"] = "猫", ["jellyfish"] = "水母",
            ["avatar"] = "模型", ["eye"] = "眼睛", ["eyes"] = "眼睛", ["face"] = "脸部",
            ["left"] = "左", ["right"] = "右", ["upper"] = "上", ["lower"] = "下",
            ["front"] = "前", ["back"] = "后", ["small"] = "小", ["big"] = "大",
            ["camera"] = "摄像机", ["light"] = "灯光", ["material"] = "材质", ["materials"] = "材质",
            ["texture"] = "贴图", ["textures"] = "贴图", ["normal"] = "法线",
            ["mesh"] = "网格", ["meshRenderer"] = "网格渲染器", ["prefab"] = "预制体", ["prefabs"] = "预制体",
            ["model"] = "模型", ["models"] = "模型", ["animation"] = "动画", ["animations"] = "动画",
            ["animator"] = "动画器", ["controller"] = "控制器", ["outfit"] = "服装", ["outfits"] = "服装",
            ["expression"] = "表情", ["expressions"] = "表情", ["parameters"] = "参数", ["icons"] = "图标",
            ["menu"] = "菜单", ["clip"] = "剪辑", ["blend"] = "混合", ["tree"] = "树",
            ["sample"] = "示例", ["samples"] = "示例", ["tool"] = "工具", ["tools"] = "工具",
            ["mobile"] = "移动端", ["option"] = "选项", ["gesture"] = "手势", ["locomotion"] = "移动",
            ["extra"] = "额外", ["generated"] = "生成内容", ["version"] = "版本"
        };

        static readonly Dictionary<string, string> ShapeBases = new Dictionary<string, string>(Comparer)
        {
            ["eye"] = "眼睛", ["eye_pupil"] = "瞳孔", ["eye_highlight_main"] = "主高光",
            ["eye_highlight_sub1"] = "副高光 1", ["eye_highlight_sub2"] = "副高光 2",
            ["eyelid"] = "眼睑", ["eyelid_shadow"] = "眼睑阴影", ["eyelash"] = "睫毛",
            ["eyelash_sub1"] = "副睫毛 1", ["eyelash_sub2"] = "副睫毛 2",
            ["eyelash_sub3"] = "副睫毛 3", ["eyelash_sub4"] = "副睫毛 4",
            ["mouth"] = "嘴部", ["brow"] = "眉毛", ["cheek"] = "脸颊"
        };

        static readonly Dictionary<string, string> ShapeActions = new Dictionary<string, string>(Comparer)
        {
            ["up"] = "上移", ["down"] = "下移", ["left"] = "左移", ["right"] = "右移",
            ["front"] = "前移", ["back"] = "后移", ["outside"] = "外移", ["center"] = "居中",
            ["on"] = "开启", ["off"] = "关闭", ["big"] = "变大", ["small"] = "变小",
            ["slim"] = "变细", ["goat"] = "山羊形", ["star"] = "星形"
        };

        internal static string Translate(NativeAliasSurface surface, string original)
        {
            if (original.Length > 256) return original;
            var trimmed = original.Trim();
            if (Exact.TryGetValue(trimmed, out var exact)) return exact;
            if (surface == NativeAliasSurface.BlendShape)
                return TranslateShape(trimmed) ?? original;

            var side = "";
            if (trimmed.EndsWith(".L", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(".R", StringComparison.OrdinalIgnoreCase))
            {
                side = trimmed.EndsWith(".L", StringComparison.OrdinalIgnoreCase) ? "左" : "右";
                trimmed = trimmed.Substring(0, trimmed.Length - 2);
                if (Exact.TryGetValue(trimmed, out exact)) return side + exact;
            }

            var tokens = WordBreak.Split(trimmed).Where(token => token.Length != 0).ToArray();
            if (tokens.Length == 0) return original;
            var translated = new List<string>(tokens.Length);
            var anyWord = false;
            foreach (var token in tokens)
            {
                if (Words.TryGetValue(token, out var term))
                {
                    anyWord = true;
                    translated.Add(term);
                }
                else if (token.All(char.IsDigit))
                    translated.Add(" " + token);
                else
                    return original;
            }
            return anyWord ? side + string.Concat(translated) : original;
        }

        static string TranslateShape(string text)
        {
            if (Comparer.Equals(text.Trim('-'), "EYE_CUSTOM")) return "── 自定义眼睛 ──";
            if (Comparer.Equals(text, "BlendShapes")) return "混合形状";
            if (Comparer.Equals(text, "eye_pupil_slim_big")) return "细瞳孔变大";
            if (ShapeBases.TryGetValue(text, out var standalone)) return standalone;

            var variant = EyeVariant.Match(text);
            if (variant.Success)
                return WithNumber(Comparer.Equals(variant.Groups["variant"].Value, "star") ? "星形眼" : "花形眼", variant.Groups["number"].Value);

            var match = ShapeSuffix.Match(text);
            if (!match.Success || !ShapeBases.TryGetValue(match.Groups["m"].Value, out var part)) return null;
            var suffix = match.Groups["direction"].Value;
            string result;
            if (Comparer.Equals(suffix, "only")) result = "仅显示" + part;
            else if (Comparer.Equals(suffix, "white")) result = "白色" + part;
            else if (ShapeActions.TryGetValue(suffix, out var action)) result = part + action;
            else return null;
            return WithNumber(result, match.Groups["number"].Value);
        }

        static string WithNumber(string text, string suffix) => suffix.Length == 0 ? text : text + " " + suffix;
    }
}
