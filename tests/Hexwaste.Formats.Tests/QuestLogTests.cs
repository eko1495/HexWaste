using System.Text;
using Hexwaste.Formats;

namespace Hexwaste.Formats.Tests;

public class QuestLogTests
{
    [Fact]
    public void ParsesRowsSkippingCommentsAndBlanks()
    {
        const string text = """
            #
            # Quest descriptions
            #

            # Arroyo Quests
            1500, 100, 9, 2, 6
            1500, 130, 480, 0, 1
            // a slash comment
            1501, 200, 100, 1, 2
            """;
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(text));

        IReadOnlyList<Quest> quests = QuestLog.Parse(stream);

        Assert.Equal(3, quests.Count);
        Assert.Equal(new Quest(1500, 100, 9, 2, 6), quests[0]);
        Assert.Equal(new Quest(1500, 130, 480, 0, 1), quests[1]); // displayThreshold 0 = always shown
        Assert.Equal(new Quest(1501, 200, 100, 1, 2), quests[2]);
    }

    [Fact]
    public void IgnoresMalformedRows()
    {
        const string text = "1500, 100, 9\n1500, 100, 9, 2, 6\nnot, a, number, here, x\n";
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(text));

        IReadOnlyList<Quest> quests = QuestLog.Parse(stream);

        Assert.Single(quests); // only the one well-formed 5-int row survives
        Assert.Equal(9, quests[0].Gvar);
    }
}
