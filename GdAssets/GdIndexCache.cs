namespace GdAssets;

/// <summary>
/// 分层文件索引缓存：按"层根目录"缓存枚举结果并带新鲜度窗口——
/// 大目录（如 20 万文件的基础游戏）只扫一次，短时间内的多次浏览直接复用；
/// 过期或强制刷新时才重新枚举该层，其余层继续用缓存。
/// 线程安全（UI 并发调用安全）。无 UI 依赖。
/// </summary>
public sealed class GdIndexCache
{
    private sealed class LayerCache
    {
        public required Dictionary<string, string> Map;
        public DateTimeOffset BuiltAt;
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, LayerCache> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>缓存新鲜窗口：窗口内的层直接复用，不重新枚举。默认 3 秒。</summary>
    public TimeSpan FreshWindow { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>清空全部缓存（下次浏览强制全量重扫）。</summary>
    public void Clear()
    {
        lock (_lock) _cache.Clear();
    }

    /// <summary>
    /// 取合并索引：各可用层 先查缓存 / 过期则重扫（forceRefresh=true 时无视新鲜度），
    /// 再按 Base → MOD 顺序合并。IO 在后台线程，UI 可 await。
    /// </summary>
    public async Task<GdIndex> GetAsync(GdInstall install, bool forceRefresh = false, CancellationToken ct = default)
    {
        var layers = install.ActiveLayers();

        // 逐层取缓存或标记需要重建（锁内只做查表，枚举放锁外）
        var layerMeta = new List<(GdLayer Layer, string Root, DateTimeOffset BuiltAt)>(layers.Count);
        var maps = new Dictionary<string, Dictionary<string, string>?>(StringComparer.OrdinalIgnoreCase);
        var toBuild = new List<(GdLayer Layer, string Root)>();

        lock (_lock)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (var (layer, root) in layers)
            {
                if (_cache.TryGetValue(root, out var hit) &&
                    !forceRefresh && now - hit.BuiltAt < FreshWindow)
                {
                    _cache[root] = hit;   // 触碰（便于调试观察）
                    layerMeta.Add((layer, root, hit.BuiltAt));
                    maps[root] = hit.Map;
                }
                else
                {
                    layerMeta.Add((layer, root, now));
                    maps[root] = null;
                    toBuild.Add((layer, root));
                }
            }
        }

        await Task.WhenAll(toBuild.Select(b => Task.Run(() =>
        {
            var map = GdIndex.EnumerateLayerSafe(b.Root, ct);
            lock (_lock) _cache[b.Root] = new LayerCache { Map = map, BuiltAt = DateTimeOffset.UtcNow };
        }, ct)).ToArray()).ConfigureAwait(false);

        lock (_lock)
        {
            for (int i = 0; i < layerMeta.Count; i++)
            {
                var (_, root, _) = layerMeta[i];
                if (maps[root] == null && _cache.TryGetValue(root, out var fresh))
                {
                    maps[root] = fresh.Map;
                    layerMeta[i] = (layerMeta[i].Layer, root, fresh.BuiltAt);
                }
            }
            var final = layerMeta.Select(m => (m.Layer, m.Root, m.BuiltAt)).ToArray();
            var mapList = layerMeta.Select(m => maps[m.Root]).ToArray();
            return GdIndex.Merge(final, mapList);
        }
    }
}
