using System.Text;
using CreoToolkit.App;

namespace CreoToolkit.Samples.ProtkAppls.Core;

/// <summary>
/// 从 SampleCommandMetadata.All 生成 Creo msg 文件内容。
/// 按 All 数组顺序输出命令段，非命令段在锚点位置插入。
/// </summary>
public static class MsgFileGenerator
{
    /// <summary>生成 msg 文件全部内容（ASCII，LF 行尾，末尾空行）。</summary>
    public static string Generate(CommandMetadata[] metadata)
    {
        ThrowUtil.IfNull(metadata);
        var sb = new StringBuilder(4096);

        foreach (var m in metadata)
        {
            EmitPreSegments(sb, m.Name);

            // label 条目
            var labelKey = m.LabelKey;
            var label = m.Label ?? throw new InvalidOperationException(
                $"命令 '{m.Name}' 缺少 Label 文本");
            EmitEntry(sb, labelKey, label);

            // help 条目
            var helpKey = m.ResolvedHelpKey;
            var help = m.Help ?? throw new InvalidOperationException(
                $"命令 '{m.Name}' 缺少 Help 文本");
            EmitEntry(sb, helpKey, help);

            EmitPostSegments(sb, m.Name);
        }

        return sb.ToString();
    }

    private static void EmitEntry(StringBuilder sb, string key, string text)
    {
        sb.Append(key).Append('\n');
        sb.Append(text).Append('\n');
        sb.Append('#').Append('\n');
        sb.Append('#').Append('\n');
    }

    private static void EmitPreSegments(StringBuilder sb, string cmdName)
    {
        if (cmdName == "ext.creo-menu.main")
        {
            EmitEntry(sb, "EXT_CREO_MENU_LABEL", "ExtCreoMenu\nExtCreoMenu menu");
            EmitEntry(sb, "EXT_CREO_MENU_MAIN_LABEL", "MainMenu\nMainMenu (push button group)");
        }
    }

    private static void EmitPostSegments(StringBuilder sb, string cmdName)
    {
        if (cmdName == "pt.agent.demo")
        {
            EmitEntry(sb, "PT_AGENT_DEMO_NO_SESSION", "No Creo session available for Agent demo");
        }
        else if (cmdName == "ext.creo-menu.main")
        {
            EmitEntry(sb, "EXT_CREO_MENU_RADIO_LABEL", "RadioButtonMenu\nRadioButtonMenu (radio group)");
            EmitEntry(sb, "EXT_CREO_MENU_RADIO_GROUP", "RadioButtonGroup\nRadio group containing 4 items");
            EmitEntry(sb, "EXT_CREO_MENU_RADIO_DESC", "ExtCreoMenu radio group description");
            EmitEntry(sb, "EXT_CREO_MENU_RADIO_ITEM1_LABEL", "RadioItem1\nRadio item 1");
            EmitEntry(sb, "EXT_CREO_MENU_RADIO_ITEM1_HELP", "Select to set radio value to item1");
            EmitEntry(sb, "EXT_CREO_MENU_RADIO_ITEM2_LABEL", "RadioItem2\nRadio item 2");
            EmitEntry(sb, "EXT_CREO_MENU_RADIO_ITEM2_HELP", "Select to set radio value to item2");
            EmitEntry(sb, "EXT_CREO_MENU_RADIO_ITEM3_LABEL", "RadioItem3\nRadio item 3");
            EmitEntry(sb, "EXT_CREO_MENU_RADIO_ITEM3_HELP", "Select to set radio value to item3");
            EmitEntry(sb, "EXT_CREO_MENU_RADIO_ITEM4_LABEL", "RadioItem4\nRadio item 4");
            EmitEntry(sb, "EXT_CREO_MENU_RADIO_ITEM4_HELP", "Select to set radio value to item4");
            EmitEntry(sb, "EXT_CREO_MENU_CHECK_LABEL", "CheckButtonMenu\nCheckButtonMenu (toggle group)");
            EmitEntry(sb, "EXT_CREO_MENU_CHECK_ITEM_LABEL", "CheckButtonItem\nToggle the check button state");
            EmitEntry(sb, "EXT_CREO_MENU_CHECK_ITEM_HELP", "Click to toggle and write JSONL log");
            EmitEntry(sb, "EXT_CREO_MENU_MAIN_DIALOG", "MainMenu push handler invoked (JSONL written)");
            EmitEntry(sb, "EXT_CREO_MENU_RADIO_PICKED", "Radio item picked");
            EmitEntry(sb, "EXT_CREO_MENU_CHECK_TOGGLED", "Check toggled");
        }
        else if (cmdName == "pt.udf.subscribe-block-ungroup")
        {
            EmitEntry(sb, "PT_UDF_NO_SESSION", "No active Creo session; load a model before subscribing.");
        }
        else if (cmdName == "pt.agent.selection-pick-highlight-release-test")
        {
            EmitEntry(sb, "CTK_USER_MSG", "%0w");
        }
        else if (cmdName == "pt.model.survey")
        {
            EmitEntry(sb, "CTK_ROOT_MENU_LABEL", "CreoToolkit");
        }
    }
}
