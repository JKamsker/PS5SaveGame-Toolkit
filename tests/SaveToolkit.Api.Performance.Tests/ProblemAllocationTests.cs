using System.Collections;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Api.Performance.Tests;

[Collection(ApiMeasurementCollection.Name)]
public sealed class ProblemAllocationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ErrorPayloadIsTraversedOnlyWhenWritingTheResponse(bool preview)
    {
        var changes = new CountedList<PlannedChange>([new("field", new string('x', 512_000), FieldValue.Of(1), FieldValue.Of(2))]);
        var body = new ActionPreview(null, 7, new ActionScope(), new ActionPlan
        {
            Changes = changes, Errors = [new("field", "Rejected")],
        }) { Stale = true };
        var edits = new CountedList<ValueChange>([new("field", FieldValue.Of(1), FieldValue.Of(2))]);
        object original = preview ? body : new SessionEditResult
        {
            Changes = edits, Errors = [new("field", "Rejected")], State = new(true, false, true) { Revision = 7 }, Stale = true,
        };
        var result = Assert.IsAssignableFrom<IResult>(ApiProblem.Normalize(Results.UnprocessableEntity(original)));
        Assert.Equal(0, preview ? changes.Enumerations : edits.Enumerations);

        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var output = new MemoryStream();
        context.Response.Body = output;
        await result.ExecuteAsync(context);
        Assert.Equal(1, preview ? changes.Enumerations : edits.Enumerations);
        using var actual = JsonDocument.Parse(output.ToArray());
        var expected = JsonSerializer.SerializeToElement(original, original.GetType(), ToolkitJson.Options);
        foreach (var property in expected.EnumerateObject())
            Assert.True(JsonElement.DeepEquals(property.Value, actual.RootElement.GetProperty(property.Name)), property.Name);
        Assert.Equal(422, actual.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Rejected", actual.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void LargePreviewNormalizationDoesNotAllocateAJsonCopy()
    {
        var preview = new ActionPreview(null, 0, new ActionScope(), new ActionPlan
        {
            Changes = [new("field", new string('x', 1_000_000), FieldValue.Of(1), FieldValue.Of(2))],
            Errors = [new("field", "Rejected")],
        });
        _ = ApiProblem.Normalize(Results.UnprocessableEntity(preview));
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = ApiProblem.Normalize(Results.UnprocessableEntity(preview));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        new ApiMeasurement(output, "1MB-preview").Record("normalize", allocated);
        Assert.True(allocated < 16_384, $"Normalizing a preview allocated {allocated:N0} B.");
    }

    [Fact]
    public void SuccessfulResponsesDoNotAllocateProblemMetadata()
    {
        var response = Results.Ok(new { value = 42 });
        _ = ApiProblem.Normalize(response);
        object? normalized = null;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) normalized = ApiProblem.Normalize(response);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Same(response, normalized);
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OptionalFieldsAndFallbackBodiesKeepTheCompleteWireContract(int kind)
    {
        object body = kind switch
        {
            0 => new ErrorDto("Rejected"),
            1 => new ErrorDto("Rejected", "custom-code"),
            2 => new SessionEditResult
            {
                Message = "Additional context", StructureChanged = true, State = new(true, true, true) { Revision = 8 },
                Warnings = ["Warning"], Errors = [new("field", "Rejected")],
            },
            3 => new ActionPreview("plan-id", 8, new ActionScope(["row"]), new ActionPlan { Errors = [new("field", "Rejected")] }),
            _ => new { custom = new { answer = 42 }, optional = (string?)null },
        };
        var expected = JsonSerializer.SerializeToElement(body, body.GetType(), ToolkitJson.Options).EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value);
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var output = new MemoryStream();
        context.Response.Body = output;
        var result = ApiProblem.Create(422, body);
        Assert.Same(result, ApiProblem.Normalize(result));
        await result.ExecuteAsync(context);
        using var actual = JsonDocument.Parse(output.ToArray());
        foreach (var property in expected)
            Assert.True(JsonElement.DeepEquals(property.Value, actual.RootElement.GetProperty(property.Key)), property.Key);
        string[] metadata = ["type", "title", "status", "detail", "error"];
        Assert.Equal(expected.Keys.Concat(metadata).Distinct().Order(), actual.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.StartsWith("application/json", context.Response.ContentType);
    }

    private sealed class CountedList<T>(IReadOnlyList<T> values) : IReadOnlyList<T>
    {
        public int Enumerations { get; private set; }
        public int Count => values.Count;
        public T this[int index] => values[index];
        public IEnumerator<T> GetEnumerator() { Enumerations++; return values.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
