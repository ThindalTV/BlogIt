using BlogIt.Services;
using BlogIt.Shared.Entities;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

public class AiHistoryCompactionTests
{
    private static List<AiMessage> MakeMessages(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new AiMessage
            {
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"message-{i}",
                CreatedAt = DateTime.UnixEpoch.AddMinutes(i)
            })
            .ToList();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(19)]
    public void SelectCompactionBatch_BelowThreshold_CompactsNothing(int count)
    {
        var messages = MakeMessages(count);

        var (toCompact, remaining) = OpenAiService.SelectCompactionBatch(messages, OpenAiService.HistoryCompactionThreshold);

        toCompact.Should().BeEmpty();
        remaining.Should().Equal(messages);
    }

    [Fact]
    public void SelectCompactionBatch_AtThreshold_CompactsOldestHalf()
    {
        var messages = MakeMessages(20);

        var (toCompact, remaining) = OpenAiService.SelectCompactionBatch(messages, 20);

        toCompact.Should().Equal(messages.Take(10));
        remaining.Should().Equal(messages.Skip(10));
    }

    [Fact]
    public void SelectCompactionBatch_OddCountAboveThreshold_RoundsDownCompactedHalf()
    {
        // 21 messages, threshold 20: compactCount = 21/2 = 10 (integer division), so the
        // remaining half is slightly larger than the compacted half rather than the reverse —
        // never compact away more than half.
        var messages = MakeMessages(21);

        var (toCompact, remaining) = OpenAiService.SelectCompactionBatch(messages, 20);

        toCompact.Should().HaveCount(10);
        remaining.Should().HaveCount(11);
        toCompact.Should().Equal(messages.Take(10));
        remaining.Should().Equal(messages.Skip(10));
    }

    [Fact]
    public void SelectCompactionBatch_RepeatedRounds_OnlyTriggersEveryHalfThreshold()
    {
        // Models the user-specified policy: hit N, compact the oldest 50%, keep going; the next
        // compaction only fires after another N/2 messages accumulate on top of the remainder.
        const int threshold = 20;
        var messages = MakeMessages(threshold); // starts right at the trigger point

        var (round1Compact, round1Remaining) = OpenAiService.SelectCompactionBatch(messages, threshold);
        round1Compact.Should().HaveCount(10);
        round1Remaining.Should().HaveCount(10);

        // Adding fewer than threshold/2 new messages must not trigger another round.
        var notYet = round1Remaining.Concat(MakeMessages(9)).ToList();
        var (round2NoTrigger, _) = OpenAiService.SelectCompactionBatch(notYet, threshold);
        round2NoTrigger.Should().BeEmpty();

        // The 10th new message brings it back to exactly the threshold and triggers again.
        var readyAgain = round1Remaining.Concat(MakeMessages(10)).ToList();
        var (round2Compact, round2Remaining) = OpenAiService.SelectCompactionBatch(readyAgain, threshold);
        round2Compact.Should().HaveCount(10);
        round2Remaining.Should().HaveCount(10);
    }
}
