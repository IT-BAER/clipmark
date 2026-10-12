using Clipmark.Core.History;

namespace Clipmark.Core.Tests;

public sealed class HistoryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("clipmark-history-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // Stand-in for DPAPI: reversible and visibly different from the input.
    private static byte[] Protect(byte[] data) => [0xEE, .. data.Reverse()];
    private static byte[]? Unprotect(byte[] data) => data is [0xEE, .. var rest] ? [.. rest.Reverse()] : null;

    private HistoryStore Store(int maxItems = 25, long maxBytes = long.MaxValue) => new(_dir, Protect, Unprotect, maxItems, maxBytes);

    private static readonly DateTime Noon = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Store_keeps_items_encrypted_on_disk_and_lists_them_newest_first()
    {
        var store = Store();
        string first = store.Add([1, 2, 3], Noon);
        string second = store.Add([4, 5], Noon.AddSeconds(1));

        Assert.Equal([second, first], store.List().Select(e => e.Id));
        Assert.Equal([1, 2, 3], store.Load(first));
        Assert.Equal([0xEE, 5, 4], File.ReadAllBytes(Directory.GetFiles(_dir).Single(f => f.Contains(second))));
    }

    [Fact]
    public void Store_gives_captures_in_the_same_millisecond_distinct_ids_in_time_order()
    {
        var store = Store();
        var ids = Enumerable.Range(0, 11).Select(n => store.Add([(byte)n], Noon)).ToList();

        Assert.Equal(11, ids.Distinct().Count());
        Assert.Equal(Enumerable.Reverse(ids), store.List().Select(e => e.Id));
        Assert.All(store.List(), e => Assert.Equal(Noon, e.TakenUtc));
    }

    [Fact]
    public void Adding_beyond_the_limit_deletes_the_oldest_item()
    {
        var store = Store(maxItems: 2);
        string first = store.Add([1], Noon);
        string second = store.Add([2], Noon.AddSeconds(1));
        string third = store.Add([3], Noon.AddSeconds(2));

        Assert.Equal([third, second], store.List().Select(e => e.Id));
        Assert.Null(store.Load(first));
    }

    [Fact]
    public void Update_replaces_the_content_and_keeps_the_position()
    {
        var store = Store();
        string first = store.Add([1], Noon);
        string second = store.Add([2], Noon.AddSeconds(1));

        Assert.True(store.Update(first, [9, 9]));

        Assert.Equal([second, first], store.List().Select(e => e.Id));
        Assert.Equal([9, 9], store.Load(first));
    }

    [Fact]
    public void Update_of_an_evicted_item_does_not_bring_it_back()
    {
        var store = Store();
        string id = store.Add([1], Noon);
        store.Delete(id);

        Assert.False(store.Update(id, [2]));
        Assert.Empty(store.List());
    }

    [Fact]
    public void Item_that_cannot_be_decrypted_loads_as_null()
    {
        var store = Store();
        string id = store.Add([1], Noon);
        File.WriteAllBytes(Path.Combine(_dir, id + ".cmh"), [1, 2, 3]); // e.g. sealed for another Windows user

        Assert.Null(store.Load(id));
    }

    [Theory]
    [InlineData(@"..\settings")]
    [InlineData("20261012-120000000.cmh")]
    [InlineData("")]
    public void Ids_that_are_not_history_ids_are_rejected(string id)
    {
        File.WriteAllBytes(Path.Combine(_dir, "settings.cmh"), [0xEE, 1]);
        var store = Store();

        Assert.Null(store.Load(id));
        Assert.False(store.Update(id, [1]));
        store.Delete(id);
        Assert.True(File.Exists(Path.Combine(_dir, "settings.cmh")));
    }

    [Fact]
    public void List_ignores_temp_and_foreign_files()
    {
        var store = Store();
        string id = store.Add([1], Noon);
        File.WriteAllBytes(Path.Combine(_dir, "20261012-130000000.cmh.tmp"), [0xEE]);
        File.WriteAllBytes(Path.Combine(_dir, "notes.cmh"), [0xEE]);
        File.WriteAllBytes(Path.Combine(_dir, "99999999-999999999.cmh"), [0xEE]); // id shape, impossible date

        Assert.Equal([id], store.List().Select(e => e.Id));
    }

    [Fact]
    public void Add_succeeds_when_an_item_to_evict_is_locked()
    {
        var store = Store(maxItems: 1);
        string old = store.Add([1], Noon);
        string fresh;
        using (new FileStream(Path.Combine(_dir, old + ".cmh"), FileMode.Open, FileAccess.Read, FileShare.None)) // e.g. antivirus scan
            fresh = store.Add([2], Noon.AddSeconds(1));

        Assert.Equal([2], store.Load(fresh));
    }

    [Fact]
    public void Clear_removes_every_file_of_the_history_folder()
    {
        var store = Store();
        store.Add([1], Noon);
        File.WriteAllBytes(Path.Combine(_dir, "20261012-130000000.cmh.tmp"), [0xEE]);

        store.Clear();

        Assert.Empty(store.List());
        Assert.Empty(Directory.GetFiles(_dir));
    }

    private static HistoryEntry Entry(int n, long size = 100) => new($"20261012-{n:D9}", size);

    [Fact]
    public void Policy_evicts_the_oldest_items_beyond_the_count_limit()
    {
        var entries = Enumerable.Range(1, 27).Select(n => Entry(n)).Reverse().ToList(); // newest first

        var evicted = HistoryPolicy.Evict(entries, maxItems: 25, maxBytes: long.MaxValue);

        Assert.Equal([Entry(1), Entry(2)], evicted);
    }

    [Fact]
    public void Policy_evicts_old_items_until_the_total_size_fits()
    {
        List<HistoryEntry> entries = [Entry(4, 400), Entry(3, 300), Entry(2, 200), Entry(1, 100)];

        var evicted = HistoryPolicy.Evict(entries, maxItems: 25, maxBytes: 800);

        Assert.Equal([Entry(1, 100), Entry(2, 200)], evicted);
    }

    [Fact]
    public void Policy_keeps_the_newest_item_even_when_it_alone_exceeds_the_size_limit()
    {
        List<HistoryEntry> entries = [Entry(2, 900), Entry(1, 100)];

        var evicted = HistoryPolicy.Evict(entries, maxItems: 25, maxBytes: 800);

        Assert.Equal([Entry(1, 100)], evicted);
    }
}
