using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherMutationRobustnessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeededMutationsEitherRejectClearlyOrPreserveTheEntireInput(bool mutateExpanded)
    {
        const int mutationCount = 256;
        var random = new Random(0x5373);
        var original = SyntheticPlayerSave.Create();
        var source = mutateExpanded ? WitcherContainer.Parse(original).Payload : original;
        for (var mutation = 0; mutation < mutationCount; mutation++)
        {
            var changed = source.ToArray();
            var position = random.Next(changed.Length);
            var mask = (byte)random.Next(1, 256);
            changed[position] ^= mask;
            var bytes = mutateExpanded ? SyntheticSave.Wrap(changed) : changed;
            try
            {
                using var document = RobustnessSave.OpenContainer(bytes);
                _ = document.BuildView();
                _ = Assert.IsAssignableFrom<IQueryableDocument>(document).Collections;
                Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
            }
            catch (SaveFormatException error)
            {
                Assert.False(string.IsNullOrWhiteSpace(error.Message));
            }
        }
    }
}
