using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nltsql.Infrastructure.Configuration;
using Nltsql.Infrastructure.Planning;
using Shouldly;

namespace Nltsql.Tests.Planning;

/// <summary>
/// Covers the planner's contract with Ollama and the ways a local model
/// gets it wrong. What it does not cover is whether a real model produces
/// good plans — that needs a real model.
/// </summary>
public sealed class OllamaQueryPlannerTests
{
    private const string ValidPlan = """
        {
          "answerable": true,
          "interpretation": "OEE je Maschine.",
          "view": "fertigung",
          "measures": ["oee"],
          "dimensions": ["machine_name"]
        }
        """;

    [Fact]
    public async Task Returns_a_validated_query()
    {
        var (planner, _) = Build(new StubHttpMessageHandler().RespondingWith(ValidPlan));

        var plan = await planner.PlanAsync("OEE je Maschine", TestModel.Model);

        plan.IsSuccess.ShouldBeTrue(plan.Failure);
        plan.Query!.View.ShouldBe("fertigung");
        plan.Query.Measures.ShouldBe(["oee"]);
        plan.Query.Dimensions.ShouldBe(["machine_name"]);
        plan.Interpretation.ShouldBe("OEE je Maschine.");
        plan.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task Sends_the_catalogue_and_the_schema()
    {
        var handler = new StubHttpMessageHandler().RespondingWith(ValidPlan);
        var (planner, _) = Build(handler);

        await planner.PlanAsync("OEE je Maschine", TestModel.Model);

        var request = handler.Requests.ShouldHaveSingleItem();

        // The catalogue is what keeps the model to real member names, and
        // the schema is what keeps the answer parseable.
        request.ShouldContain("Datenbereich");
        request.ShouldContain("machine_name");
        request.ShouldContain("\"format\"");
        request.ShouldContain("\"stream\":false");
        request.ShouldContain("\"num_ctx\":8192");
    }

    [Fact]
    public async Task Reads_a_fenced_reply()
    {
        var fenced = $"```json\n{ValidPlan}\n```";
        var (planner, _) = Build(new StubHttpMessageHandler().RespondingWith(fenced));

        var plan = await planner.PlanAsync("OEE je Maschine", TestModel.Model);

        plan.IsSuccess.ShouldBeTrue(plan.Failure);
    }

    [Fact]
    public async Task Reports_the_model_reason_when_not_answerable()
    {
        var reply = """
            {"answerable": false, "reason": "Zu Lieferanten gibt es keine Daten.", "measures": [], "dimensions": []}
            """;

        var (planner, _) = Build(new StubHttpMessageHandler().RespondingWith(reply));

        var plan = await planner.PlanAsync("Umsatz je Lieferant", TestModel.Model);

        plan.IsSuccess.ShouldBeFalse();
        plan.Failure.ShouldBe("Zu Lieferanten gibt es keine Daten.");
    }

    [Fact]
    public async Task Repairs_an_unknown_member_and_succeeds()
    {
        var wrong = """
            {
              "answerable": true,
              "interpretation": "OEE je Anlage.",
              "view": "fertigung",
              "measures": ["oee"],
              "dimensions": ["maschine"]
            }
            """;

        var handler = new StubHttpMessageHandler()
            .RespondingWith(wrong)
            .RespondingWith(ValidPlan);

        var (planner, _) = Build(handler);

        var plan = await planner.PlanAsync("OEE je Maschine", TestModel.Model);

        plan.IsSuccess.ShouldBeTrue(plan.Failure);
        plan.Attempts.ShouldBe(2);

        handler.Requests.Count.ShouldBe(2);

        // The second round has to name the offending member and the
        // suggestion, otherwise it is just a re-roll of the same mistake.
        handler.Requests[1].ShouldContain("maschine");
        handler.Requests[1].ShouldContain("machine_name");
    }

    [Fact]
    public async Task Gives_up_after_the_configured_repair_attempts()
    {
        var wrong = """
            {"answerable": true, "view": "fertigung", "measures": ["umsatz"], "dimensions": []}
            """;

        var handler = new StubHttpMessageHandler()
            .RespondingWith(wrong)
            .RespondingWith(wrong);

        var (planner, _) = Build(handler);

        var plan = await planner.PlanAsync("Umsatz", TestModel.Model);

        plan.IsSuccess.ShouldBeFalse();
        plan.Attempts.ShouldBe(2);
        plan.Errors.ShouldNotBeEmpty();
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Explains_a_missing_model_instead_of_throwing()
    {
        var handler = new StubHttpMessageHandler()
            .Responding(HttpStatusCode.NotFound, """{"error":"model \"qwen2.5:14b-instruct\" not found"}""");

        var (planner, _) = Build(handler);

        var plan = await planner.PlanAsync("OEE je Maschine", TestModel.Model);

        plan.IsSuccess.ShouldBeFalse();
        plan.Failure.ShouldNotBeNull().ShouldContain("ollama pull qwen2.5:14b-instruct");
    }

    [Fact]
    public async Task Explains_an_unreachable_server_instead_of_throwing()
    {
        var handler = new StubHttpMessageHandler()
            .Throwing(new HttpRequestException("Connection refused"));

        var (planner, _) = Build(handler);

        var plan = await planner.PlanAsync("OEE je Maschine", TestModel.Model);

        plan.IsSuccess.ShouldBeFalse();
        plan.Failure.ShouldNotBeNull().ShouldContain("nicht erreichbar");
        plan.Failure.ShouldNotBeNull().ShouldContain("http://localhost:11434");
    }

    [Fact]
    public async Task Reports_a_reply_that_carries_no_json()
    {
        var (planner, _) = Build(new StubHttpMessageHandler().RespondingWith("Da bin ich überfragt."));

        var plan = await planner.PlanAsync("OEE je Maschine", TestModel.Model);

        plan.IsSuccess.ShouldBeFalse();
        plan.Failure.ShouldNotBeNull().ShouldContain("konnte nicht gelesen werden");
    }

    [Fact]
    public async Task Is_unavailable_when_switched_off()
    {
        var (planner, _) = Build(new StubHttpMessageHandler(), options => options.Enabled = false);

        planner.IsAvailable.ShouldBeFalse();

        var plan = await planner.PlanAsync("OEE je Maschine", TestModel.Model);

        plan.IsSuccess.ShouldBeFalse();
        plan.Failure.ShouldNotBeNull().ShouldContain("Planner:Enabled");
    }

    private static (OllamaQueryPlanner Planner, PlannerOptions Options) Build(
        StubHttpMessageHandler handler,
        Action<PlannerOptions>? configure = null)
    {
        var options = new PlannerOptions();
        configure?.Invoke(options);

        var client = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };

        var planner = new OllamaQueryPlanner(
            client,
            Options.Create(options),
            TimeProvider.System,
            NullLogger<OllamaQueryPlanner>.Instance);

        return (planner, options);
    }
}
