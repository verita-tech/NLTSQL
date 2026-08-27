using Nltsql.Infrastructure.Planning;
using Shouldly;

namespace Nltsql.Tests.Planning;

/// <summary>
/// Guards the one thing that got harder when planning moved to a local
/// model: the reply is shaped by a sampling grammar, not validated by a
/// server, so it can still arrive wrapped in prose or a markdown fence.
/// </summary>
public sealed class JsonPayloadTests
{
    [Fact]
    public void Returns_plain_json_unchanged()
    {
        JsonPayload.Extract("""{"answerable":true}""").ShouldBe("""{"answerable":true}""");
    }

    [Fact]
    public void Strips_a_markdown_fence()
    {
        var reply = """
            ```json
            {"answerable": true, "view": "fertigung"}
            ```
            """;

        JsonPayload.Extract(reply).ShouldBe("""{"answerable": true, "view": "fertigung"}""");
    }

    [Fact]
    public void Strips_a_fence_without_a_language_tag()
    {
        JsonPayload.Extract("```\n{\"answerable\": false}\n```").ShouldBe("""{"answerable": false}""");
    }

    [Fact]
    public void Drops_prose_before_and_after()
    {
        var reply = "Hier ist die Abfrage:\n{\"answerable\": true}\nViel Erfolg!";

        JsonPayload.Extract(reply).ShouldBe("""{"answerable": true}""");
    }

    [Fact]
    public void Keeps_nested_objects_whole()
    {
        var reply = """{"a": {"b": {"c": 1}}, "d": 2}""";

        JsonPayload.Extract(reply).ShouldBe(reply);
    }

    [Fact]
    public void Ignores_braces_inside_strings()
    {
        // A filter value may legitimately contain a brace, which is why
        // this counts braces outside strings rather than matching a regex.
        var reply = """{"values": ["Werk {Nord}"], "ok": true}""";

        JsonPayload.Extract(reply).ShouldBe(reply);
    }

    [Fact]
    public void Ignores_an_escaped_quote_inside_a_string()
    {
        var reply = """{"reason": "sagte \"nein\"", "ok": true}""";

        JsonPayload.Extract(reply).ShouldBe(reply);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Dazu habe ich keine Daten.")]
    public void Returns_null_when_there_is_no_object(string? reply)
    {
        JsonPayload.Extract(reply).ShouldBeNull();
    }

    [Fact]
    public void Returns_null_for_a_truncated_object()
    {
        // The model ran out of tokens mid-answer. Half a plan is not a
        // plan, and guessing the closing braces would invent content.
        JsonPayload.Extract("""{"answerable": true, "measures": ["oee" """).ShouldBeNull();
    }
}
