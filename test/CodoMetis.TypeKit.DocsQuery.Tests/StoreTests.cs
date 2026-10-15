using DocsQuery;

namespace CodoMetis.TypeKit.DocsQuery.Tests;

/// <summary>The cache: rebuilt exactly when the fingerprint of the inputs changes, written whole or not at all.</summary>
public sealed class StoreTests : IDisposable
{
    private readonly TempRepo _repo = TempRepo.Sample();

    public void Dispose() => _repo.Dispose();

    [Fact]
    public void An_unchanged_corpus_is_read_from_the_cache()
    {
        _repo.Load().Outcome.ShouldBe(Store.Outcome.Rebuilt);
        _repo.Load().Outcome.ShouldBe(Store.Outcome.Read);
    }

    /// <summary>A timestamp is no change: a checkout or a copy that touches every file keeps the index.</summary>
    [Fact]
    public void A_new_timestamp_alone_keeps_the_cache()
    {
        _repo.Load();
        File.SetLastWriteTimeUtc(Path.Combine(_repo.Root, "docs/json.md"), DateTime.UtcNow.AddHours(1));

        _repo.Load().Outcome.ShouldBe(Store.Outcome.Read);
    }

    /// <summary>A content change is a change, even when the file keeps its old timestamp, as a restore does.</summary>
    [Fact]
    public void A_content_change_under_an_old_timestamp_rebuilds()
    {
        _repo.Load();
        var path  = Path.Combine(_repo.Root, "docs/json.md");
        var mtime = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, File.ReadAllText(path) + "\nMore.\n");
        File.SetLastWriteTimeUtc(path, mtime);

        _repo.Load().Outcome.ShouldBe(Store.Outcome.Rebuilt);
    }

    [Theory]
    [InlineData("docs/new.md")]
    [InlineData("test/CodoMetis.TypeKit.Samples/More.cs")]
    public void An_added_input_rebuilds(string path)
    {
        _repo.Load();
        _repo.With(path, "# New\n");
        _repo.Load().Outcome.ShouldBe(Store.Outcome.Rebuilt);
    }

    [Fact]
    public void Another_tool_version_rebuilds()
    {
        _repo.Load("one");
        _repo.Load("two").Outcome.ShouldBe(Store.Outcome.Rebuilt);
    }

    [Fact]
    public void A_damaged_cache_is_rebuilt()
    {
        _repo.Load();
        var bytes = File.ReadAllBytes(_repo.Cache);
        File.WriteAllBytes(_repo.Cache, bytes[..(bytes.Length / 2)]);

        var (index, outcome) = _repo.Load();
        outcome.ShouldBe(Store.Outcome.Rebuilt);
        index.Docs.Count.ShouldBe(10);
    }

    /// <summary>
    /// The cache is written to a temporary file and renamed over the old one: a reader that opened the
    /// old index reads all of it while the new one lands, and no temporary file is left behind. A write
    /// in place would truncate under the reader.
    /// </summary>
    [Fact]
    public void A_rebuild_replaces_the_cache_whole()
    {
        _repo.Load();
        var before = File.ReadAllBytes(_repo.Cache);

        using (var reader = new FileStream(_repo.Cache, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            _repo.With("docs/json.md", "# Changed\n");
            _repo.Load().Outcome.ShouldBe(Store.Outcome.Rebuilt);

            using var old = new MemoryStream();
            reader.CopyTo(old);
            old.ToArray().ShouldBe(before, "a reader of the old cache saw it change under it");
        }

        File.ReadAllBytes(_repo.Cache).ShouldNotBe(before);
        Directory.EnumerateFiles(_repo.Root, "*.tmp").ShouldBeEmpty();
    }

    /// <summary>The same inputs give the same bytes: ordinal order throughout, nothing from the clock or the culture.</summary>
    [Fact]
    public void The_index_is_deterministic()
    {
        _repo.Load();
        var first = File.ReadAllBytes(_repo.Cache);
        File.Delete(_repo.Cache);
        _repo.Load();

        File.ReadAllBytes(_repo.Cache).ShouldBe(first);
    }

    [Fact]
    public void What_is_read_is_what_was_written()
    {
        var (built, _) = _repo.Load();
        var (read, outcome) = _repo.Load();

        outcome.ShouldBe(Store.Outcome.Read);
        read.Docs.SequenceEqual(built.Docs, new DocComparer()).ShouldBeTrue();
        read.Sections.Select(s => s.Path).ShouldBe(built.Sections.Select(s => s.Path));
        read.Edges.ShouldBe(built.Edges);
        read.Claims.ShouldBe(built.Claims);
        read.Definitions.ShouldBe(built.Definitions);
        read.Terms.ShouldBe(built.Terms);
        read.Lengths.ShouldBe(built.Lengths);
    }

    private sealed class DocComparer : IEqualityComparer<Doc>
    {
        public bool Equals(Doc? x, Doc? y) => x!.Path == y!.Path && x.Kind == y.Kind && x.Title == y.Title && x.Id == y.Id && x.LineCount == y.LineCount && x.Meta.SequenceEqual(y.Meta);

        public int GetHashCode(Doc obj) => obj.Path.GetHashCode(StringComparison.Ordinal);
    }
}
