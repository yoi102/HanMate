using HanMate.Core.Audio;

namespace HanMate.Core.Tests.Audio;

public sealed class MeloVoiceInputTests
{
    [Theory]
    [InlineData("p a4 #0", "p a 4 4")]
    [InlineData("ch iii1 #0", "ch ir 1 1")]
    [InlineData("z ii3 #0", "z i0 3 3")]
    [InlineData("n v3 #0", "n v 3 3")]
    [InlineData("^ ian2 #0", "y En 2 2")]
    [InlineData("^ uei4 #0", "w ei 4 4")]
    [InlineData("l iou2 #0", "l iu 2 2")]
    [InlineData("^ ong1 #0", "ong 1")]
    [InlineData("^ in2 #0 h ang2 #0", "y in h ang 2 2 2 2")]
    public void KeepsCompleteSyllablesAndPerPhoneTones(string input, string expected)
        => Assert.Equal(expected, MeloVoiceInput.FromTeachingPhonemes(input));
    [Theory]
    [InlineData("")] [InlineData("b a0 #0")] [InlineData("b a1 #1")]
    public void RejectsMalformedReadings(string input)
        => Assert.Throws<InvalidDataException>(() => MeloVoiceInput.FromTeachingPhonemes(input));
}
