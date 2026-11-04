namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     Tests that every example documented on the PartOfSpeech public API compiles against the real API.
/// </summary>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Every documented example in the shipped XML documentation file compiles.
    /// </summary>
    [Fact]
    public void PartOfSpeech_XmlDocExamples_Compile()
    {
        XmlDocExampleVerifier.VerifyExamples("DemaConsulting.PartOfSpeech.xml", "DemaConsulting.PartOfSpeech");
    }
}
