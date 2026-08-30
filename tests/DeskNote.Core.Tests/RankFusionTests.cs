using DeskNote.Core.Services;

namespace DeskNote.Core.Tests;

/// <summary>
/// Fusion is where hybrid retrieval either earns its keep or quietly becomes keyword search with
/// extra latency, so the property that matters — agreement wins — is pinned here.
/// </summary>
public class RankFusionTests
{
    [Fact]
    public void A_result_both_retrievers_found_outranks_one_either_found_alone()
    {
        var keyword = new[] { "a", "b", "c" };
        var semantic = new[] { "d", "b", "e" };

        var fused = RankFusion.Fuse<string>([keyword, semantic]);

        Assert.Equal("b", fused[0]);
    }

    [Fact]
    public void One_empty_list_leaves_the_other_ranking_intact()
    {
        var fused = RankFusion.Fuse<string>([["a", "b", "c"], []]);

        Assert.Equal(["a", "b", "c"], fused);
    }

    [Fact]
    public void Nothing_found_anywhere_is_an_empty_result() =>
        Assert.Empty(RankFusion.Fuse<string>([[], []]));

    [Fact]
    public void The_limit_is_respected() =>
        Assert.Equal(2, RankFusion.Fuse<string>([["a", "b", "c", "d"]], limit: 2).Count);

    /// <summary>A retriever that returns the same note three times must not buy rank with it.</summary>
    [Fact]
    public void A_duplicate_within_one_list_scores_the_same_as_a_single_mention()
    {
        var repeated = RankFusion.Fuse<string>([["a", "a", "a"], ["b", "a"]]);
        var once = RankFusion.Fuse<string>([["a"], ["b", "a"]]);

        Assert.Equal(once, repeated);
    }

    [Fact]
    public void Ties_are_broken_by_which_retriever_saw_the_item_first()
    {
        // Both lists are the same length with no overlap, so every score is matched by one in the
        // other list; the keyword list is passed first and keeps the earlier position.
        var fused = RankFusion.Fuse<string>([["a"], ["b"]]);

        Assert.Equal(["a", "b"], fused);
    }
}
