using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nltsql.Infrastructure.Configuration;
using Nltsql.Infrastructure.Planning;
using Shouldly;

namespace Nltsql.Tests.Planning;

public sealed class OllamaQueryPlannerTests : IDisposable
{
    private const string ModelTag = "qwen2.5:7b-instruct";

    private readonly StubHttpMessageHandler _handler = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private OllamaQueryPlanner CreatePlanner(PlannerOptions? overrides = null)
    {
        var options = overrides ?? new PlannerOptions { Enabled = true, Model = ModelTag };

        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:11434") };
        var chatClient = new OllamaChatClient(httpClient, Options.Create(options));

        return new OllamaQueryPlanner(
            chatClient,
            Options.Create(options),
            _cache,
            TimeProvider.System,
            NullLogger<OllamaQueryPlanner>.Instance);
    }

    private static string ValidPlan => """
    {
      "answerable": true,
      "interpretation": "OEE je Maschine der letzten 30 Tage",
      "view": "fertigung",
      "measures": ["oee"],
      "dimensions": ["machine_name"],
      "filters": [],
      "order": [],
      "limit": 100
    }
    """;

    public void Dispose()
    {
        _handler.Dispose();
        _cache.Dispose();
    }

    private JsonElement LastChatRequest()
    {
        var body = _handler.RequestBodies.Last(b => !string.IsNullOrEmpty(b));

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Fact]
    public async Task Plans_a_valid_query()
    {
        _handler.EnqueueInstalledModels(ModelTag);
        _handler.EnqueueChatReply(ValidPlan);

        var plan = await CreatePlanner().PlanAsync("OEE je Maschine", TestModel.Model);

        plan.IsSuccess.ShouldBeTrue(plan.Failure);
        plan.Query!.Measures.ShouldBe(["oee"]);
        plan.Query.Dimensions.ShouldBe(["machine_name"]);
        plan.Interpretation.ShouldBe("OEE je Maschine der letzten 30 Tage");
    }

    [Fact]
    public async Task Sends_the_context_size_explicitly()
    {
        _handler.EnqueueInstalledModels(ModelTag);
        _handler.EnqueueChatReply(ValidPlan);

        await CreatePlanner().PlanAsync("OEE je Maschine", TestModel.Model);

        // Left unset, Ollama applies a small default and truncates the
        // prompt from the front — dropping the catalogue without any
        // error, which shows up as a planner inventing member names.
        var options = LastChatRequest().GetProperty("options");
        options.GetProperty("num_ctx").GetInt32().ShouldBe(8192);
        options.GetProperty("temperature").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Requests_a_non_streamed_completion_constrained_by_the_schema()
    {
        _handler.EnqueueInstalledModels(ModelTag);
        _handler.EnqueueChatReply(ValidPlan);

        await CreatePlanner().PlanAsync("OEE je Maschine", TestModel.Model);

        var request = LastChatRequest();

        request.GetProperty("stream").GetBoolean().ShouldBeFalse();
        request.GetProperty("model").GetString().ShouldBe(ModelTag);
        request.GetProperty("keep_alive").GetString().ShouldBe("30m");

        // Structured output: the schema goes on the request, so decoding
        // itself is constrained rather than the prompt merely asking.
        request.GetProperty("format").GetProperty("properties")
            .TryGetProperty("view", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Puts_the_catalogue_in_the_system_message()
    {
        _handler.EnqueueInstalledModels(ModelTag);
        _handler.EnqueueChatReply(ValidPlan);

        await CreatePlanner().PlanAsync("OEE je Maschine", TestModel.Model);

        var messages = LastChatRequest().GetProperty("messages");
        var system = messages[0].GetProperty("content").GetString()!;

        system.ShouldContain("fertigung");
        system.ShouldContain("oee");

        // Synonyms are what let a business term reach a technical name.
        system.ShouldContain("Anlageneffektivität");
        messages[1].GetProperty("content").GetString().ShouldBe("OEE je Maschine");
    }

    [Fact]
    public async Task Retries_with_the_validation_errors_when_a_member_is_invented()
    {
        _handler.EnqueueInstalledModels(ModelTag);

        // First reply names a measure that does not exist — the mistake a
        // small local model actually makes.
        _handler.EnqueueChatReply("""
        {"answerable":true,"interpretation":"x","view":"fertigung",
         "measures":["anlageneffektivitaet"],"dimensions":[],"filters":[],"order":[]}
        """);
        _handler.EnqueueChatReply(ValidPlan);

        var plan = await CreatePlanner().PlanAsync("Anlageneffektivität", TestModel.Model);

        plan.IsSuccess.ShouldBeTrue(plan.Failure);
        plan.Attempts.ShouldBe(2);

        // The retry has to carry the specific error, including the
        // suggestion, or it is just a re-roll of the same guess.
        var repair = LastChatRequest().GetProperty("messages")[3].GetProperty("content").GetString()!;
        repair.ShouldContain("anlageneffektivitaet");
        repair.ShouldContain("oee");
    }

    [Fact]
    public async Task Gives_up_after_the_configured_repair_attempts()
    {
        _handler.EnqueueInstalledModels(ModelTag);

        for (var i = 0; i < 3; i++)
        {
            _handler.EnqueueChatReply("""
            {"answerable":true,"interpretation":"x","view":"fertigung",
             "measures":["gibtsnicht"],"dimensions":[],"filters":[],"order":[]}
            """);
        }

        var plan = await CreatePlanner(new PlannerOptions
        {
            Enabled = true,
            Model = ModelTag,
            MaxRepairAttempts = 1,
        }).PlanAsync("Unsinn", TestModel.Model);

        // An invalid plan is reported, never executed.
        plan.IsSuccess.ShouldBeFalse();
        plan.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Passes_through_an_unanswerable_verdict()
    {
        _handler.EnqueueInstalledModels(ModelTag);
        _handler.EnqueueChatReply("""
        {"answerable":false,"reason":"Umsatzdaten sind nicht im Modell enthalten.",
         "interpretation":null,"view":null,"measures":[],"dimensions":[]}
        """);

        var plan = await CreatePlanner().PlanAsync("Wie hoch war der Umsatz?", TestModel.Model);

        plan.IsSuccess.ShouldBeFalse();
        plan.Failure.ShouldNotBeNull().ShouldContain("Umsatzdaten");
    }

    [Fact]
    public async Task Says_which_model_to_pull_when_it_is_missing()
    {
        _handler.EnqueueInstalledModels("llama3.1:8b");

        var plan = await CreatePlanner().PlanAsync("OEE je Maschine", TestModel.Model);

        plan.IsSuccess.ShouldBeFalse();
        plan.Failure.ShouldNotBeNull().ShouldContain($"ollama pull {ModelTag}");

        // Preflight must run before generation is attempted.
        _handler.RequestPaths.ShouldBe(["/api/tags"]);
    }

    [Fact]
    public async Task Explains_that_the_server_is_down_rather_than_leaking_a_socket_error()
    {
        _handler.RefuseConnections = true;

        var plan = await CreatePlanner().PlanAsync("OEE je Maschine", TestModel.Model);

        // The likeliest first-run state. "Connection refused" tells a
        // business user nothing; the name of the thing to start does.
        plan.IsSuccess.ShouldBeFalse();
        plan.Failure.ShouldNotBeNull().ShouldContain("Ollama");
    }

    [Fact]
    public async Task Reports_unusable_JSON_rather_than_guessing()
    {
        _handler.EnqueueInstalledModels(ModelTag);
        _handler.EnqueueChatReply("nicht wirklich JSON");

        var plan = await CreatePlanner().PlanAsync("OEE", TestModel.Model);

        plan.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task Is_unavailable_until_explicitly_enabled()
    {
        var planner = CreatePlanner(new PlannerOptions { Enabled = false });

        planner.IsAvailable.ShouldBeFalse();

        var plan = await planner.PlanAsync("OEE", TestModel.Model);
        plan.IsSuccess.ShouldBeFalse();
    }

    [Theory]
    [InlineData("qwen2.5:7b-instruct", "qwen2.5:7b-instruct", true)]
    [InlineData("qwen2.5", "qwen2.5:latest", true)]
    [InlineData("QWEN2.5:7B-INSTRUCT", "qwen2.5:7b-instruct", true)]
    [InlineData("qwen2.5:7b-instruct", "qwen2.5:14b-instruct", false)]
    public void Matches_a_configured_model_against_installed_tags(
        string configured, string installed, bool expected)
    {
        // Ollama reports tags fully qualified; an unqualified config
        // value means ":latest" to the user.
        OllamaChatClient.IsInstalled(configured, [installed]).ShouldBe(expected);
    }
}
