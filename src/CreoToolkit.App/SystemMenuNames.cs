namespace CreoToolkit.App;

/// <summary>Creo 4 M140 Parametric 主菜单栏系统菜单白名单。</summary>
public static class SystemMenuNames
{
    private static readonly string[] _builtin =
    [
        "File", "Help", "Info",
        "Edit", "View", "Insert", "Analysis", "Annotate",
        "Render", "Tools", "Window", "Applications",
    ];

    public static readonly IReadOnlyList<string> Builtin = Array.AsReadOnly(_builtin);

    public static ISet<string> BuildAllowList(IEnumerable<string>? appExtra = null)
    {
        var set = new HashSet<string>(Builtin, StringComparer.Ordinal);
        var envExtra = Environment.GetEnvironmentVariable(CtkEnv.AppSystemMenuExtra);
        if (!string.IsNullOrWhiteSpace(envExtra))
        {
            foreach (var name in envExtra.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = name.Trim();
                if (trimmed.Length > 0)
                    set.Add(trimmed);
            }
        }

        if (appExtra is not null)
        {
            foreach (var name in appExtra)
            {
                set.Add(name);
            }
        }

        return set;
    }
}
