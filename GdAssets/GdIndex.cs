namespace GdAssets;

/// <summary>合并后的一条文件记录：相对路径 + 实际来源层与绝对路径。</summary>
/// <param name="RelativePath">
/// 相对某层根目录的路径，含顶层 database\ 或 resources\ 前缀，统一反斜杠
/// （如 database\records\items\xxx.dbr）。合并键，大小写不敏感。
/// </param>
/// <param name="FullPath">实际落盘的绝对路径（来自最终覆盖该文件的层）。</param>
/// <param name="Layer">实际来源层（同一路径多层都有时 = 最高优先级层）。</param>
public sealed record GdFileEntry(string RelativePath, string FullPath, GdLayer Layer);

/// <summary>
/// 跨层合并文件索引：把各层根目录下 database\ 与 resources\ 内的全部文件
/// 按相对路径合并，后者（DLC1→DLC2→DLC3→MOD）覆盖前者。
/// 无 UI 依赖；构建在后台线程进行（见 <see cref="BuildAsync"/>）。
/// </summary>
public sealed class GdIndex
{
    private readonly Dictionary<string, GdFileEntry> _files;

    internal GdIndex(Dictionary<string, GdFileEntry> files,
        IReadOnlyList<(GdLayer Layer, string Root, DateTimeOffset BuiltAt)> layers)
    {
        _files = files;
        Layers = layers;
    }

    /// <summary>合并结果：键 = 相对路径（OrdinalIgnoreCase）。</summary>
    public IReadOnlyDictionary<string, GdFileEntry> Files => _files;

    /// <summary>本次合并实际采用的层与各自构建时间（缓存新鲜度判断用）。</summary>
    public IReadOnlyList<(GdLayer Layer, string Root, DateTimeOffset BuiltAt)> Layers { get; }

    public int Count => _files.Count;

    /// <summary>相对路径 → 条目；不存在返回 null。</summary>
    public GdFileEntry? Find(string relativePath) =>
        _files.TryGetValue(NormalizeKey(relativePath), out var e) ? e : null;

    internal static string NormalizeKey(string rel) => rel.Trim().Replace('/', '\\');

    /// <summary>
    /// 枚举单层目录：返回 相对路径 → 绝对路径。只收 database\ 与 resources\ 两个子树
    /// （与官方加载方式一致），递归全部子目录，跳过无法访问的目录与重解析点。
    /// </summary>
    internal static Dictionary<string, string> EnumerateLayer(string root, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };
        foreach (string sub in new[] { GdInstall.DatabaseDir, GdInstall.ResourcesDir })
        {
            string baseDir = Path.Combine(root, sub);
            if (!Directory.Exists(baseDir)) continue;
            string prefix = baseDir + Path.DirectorySeparatorChar;
            foreach (string file in Directory.EnumerateFiles(baseDir, "*", opts))
            {
                ct.ThrowIfCancellationRequested();
                // file 必然以 prefix 开头（同目录枚举）；失败则退回 GetRelativePath
                string rel = file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? file[prefix.Length..]
                    : Path.GetRelativePath(baseDir, file);
                map[sub + "\\" + rel] = file;
            }
        }
        return map;
    }

    /// <summary><see cref="EnumerateLayer"/> 的容错版：根目录消失 / IO 异常时返回空表（不中断整体合并）。</summary>
    internal static Dictionary<string, string> EnumerateLayerSafe(string root, CancellationToken ct)
    {
        try { return EnumerateLayer(root, ct); }
        catch (OperationCanceledException) { throw; }
        catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    /// <summary>
    /// 由各层已枚举的 {相对路径 → 绝对路径} 表合并成索引（按 layers 顺序，后者覆盖前者）。
    /// 层内表可为 null（该层未启用 / 枚举失败）。
    /// </summary>
    internal static GdIndex Merge(IReadOnlyList<(GdLayer Layer, string Root, DateTimeOffset BuiltAt)> layers,
        IReadOnlyList<Dictionary<string, string>?> maps)
    {
        var merged = new Dictionary<string, GdFileEntry>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < layers.Count; i++)
        {
            var map = maps[i];
            if (map == null) continue;
            var (layer, root, _) = layers[i];
            foreach (var (rel, full) in map)
                merged[rel] = new GdFileEntry(rel, full, layer);
        }
        return new GdIndex(merged, layers);
    }

    /// <summary>
    /// 后台枚举全部层并合并。逐层并行枚举（IO 密集，各层互不等待），
    /// 单层枚举失败（根目录中途被移除等）不影响其它层。
    /// </summary>
    public static async Task<GdIndex> BuildAsync(
        IReadOnlyList<(GdLayer Layer, string Root)> layers, CancellationToken ct = default)
    {
        var meta = layers.Select(l => (l.Layer, l.Root, DateTimeOffset.UtcNow)).ToArray();
        var tasks = layers.Select(l => Task.Run(() =>
        {
            try { return EnumerateLayer(l.Root, ct); }
            catch (OperationCanceledException) { throw; }
            catch { return null; }   // 根目录消失 / IO 异常：该层视为空
        }, ct)).ToArray();

        var maps = await Task.WhenAll(tasks).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return Merge(meta, maps);
    }
}
