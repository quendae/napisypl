using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class LlmJsonProtocolTests
{
    [Fact]
    public void ParseResponse_AcceptsJsonInsideCodeFence()
    {
        const string response = """
        ```json
        [{"id":1,"text":"Cześć"},{"id":2,"text":"Do widzenia"}]
        ```
        """;

        var parsed = LlmJsonProtocol.ParseResponse(response);

        Assert.Equal("Cześć", parsed[1]);
        Assert.Equal("Do widzenia", parsed[2]);
    }

    [Fact]
    public void ParseResponse_RejectsDuplicateIds()
    {
        const string response = "[{\"id\":1,\"text\":\"A\"},{\"id\":1,\"text\":\"B\"}]";

        Assert.Throws<InvalidDataException>(() => LlmJsonProtocol.ParseResponse(response));
    }
}
