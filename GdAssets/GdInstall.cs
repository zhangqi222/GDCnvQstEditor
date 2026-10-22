namespace GdAssets;

/// <summary>Grim Dawn 资源加载层：基础游戏 → DLC1..3 → MOD，后者覆盖前者。</summary>
public enum GdLayer
{
    Base = 0,
    Dlc1 = 1,
    Dlc2 = 2,
    Dlc3 = 3,
    Mod = 4,
}

/// <summary>某层目录的就绪状态：目录本身 + database / resources 子目录是否存在。</summary>
/// <remarks>官方加载方式：各层目录下只认 database 与 resources 两个子目录，
/// 二者有其一即可参与文件合并。</remarks>
public readonly record struct GdDirStatus(bool Exists, bool HasDatabase, bool HasResources)
{
    /// <summary>该层能否参与文件合并（目录存在且至少有 database / resources 之一）。</summary>
    public bool Usable => Exists && (HasDatabase || HasResources);
}

/// <summary>
/// Grim Dawn 安装目录模型（可跨项目复用，无 UI 依赖）：
/// 基础游戏 + 三个官方 DLC 目录（惯例为根目录下 GDX1/GDX2/GDX3）+ 一个 MOD 目录，
/// 文件加载顺序 Base → DLC1 → DLC2 → DLC3 → MOD，后者覆盖前者同名文件，新增文件同样生效。
/// </summary>
public sealed class GdInstall
{
    /// <summary>各层根目录（下标与 <see cref="GdLayer"/> 一致）；null / 空串 / 不存在表示该层未配置。</summary>
    public string?[] Roots { get; } = new string?[5];

    public string? GameRoot { get => Roots[0]; set => Roots[0] = Clean(value); }
    public string? Dlc1 { get => Roots[1]; set => Roots[1] = Clean(value); }
    public string? Dlc2 { get => Roots[2]; set => Roots[2] = Clean(value); }
    public string? Dlc3 { get => Roots[3]; set => Roots[3] = Clean(value); }
    public string? Mod { get => Roots[4]; set => Roots[4] = Clean(value); }

    /// <summary>官方 DLC 目录名惯例（GDX1..GDX3，位于游戏根目录下）。</summary>
    public const string DlcDirPrefix = "GDX";

    /// <summary>资源子目录名（官方固定规则：记录类 → database\，其余资源 → resources\）。</summary>
    public const string DatabaseDir = "database";
    public const string ResourcesDir = "resources";

    private static string? Clean(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim())); }
        catch { return null; }
    }

    /// <summary>由游戏根目录推出 DLC 目录的完整路径（GDX1..GDX3），不检查存在性。</summary>
    public static string AutoDlcPath(string gameRoot, int n) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(gameRoot, DlcDirPrefix + n)));

    /// <summary>检查某层目录的就绪状态（不存在 / 无权限均返回不可用）。</summary>
    public static GdDirStatus Check(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return default;
        try
        {
            if (!Directory.Exists(root)) return default;
            return new GdDirStatus(
                Exists: true,
                HasDatabase: Directory.Exists(Path.Combine(root, DatabaseDir)),
                HasResources: Directory.Exists(Path.Combine(root, ResourcesDir)));
        }
        catch { return default; }
    }

    /// <summary>当前配置的就绪状态（按层数组）。</summary>
    public GdDirStatus[] Status() =>
        Roots.Select(Check).ToArray();

    /// <summary>
    /// 参与文件合并的层（按 Base → MOD 顺序）：目录存在且至少含 database / resources 之一。
    /// 返回 (层, 该层根目录)。
    /// </summary>
    public List<(GdLayer Layer, string Root)> ActiveLayers()
    {
        var list = new List<(GdLayer, string)>();
        for (int i = 0; i < Roots.Length; i++)
        {
            var st = Check(Roots[i]);
            if (st.Usable) list.Add(((GdLayer)i, Roots[i]!));
        }
        return list;
    }

    /// <summary>是否没有任何可用层。</summary>
    public bool IsEmpty => ActiveLayers().Count == 0;
}
