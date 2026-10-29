using Microsoft.AspNetCore.Http;
using SaveToolkit.Api.Endpoints;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class CleanCloseRevisionFilterTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Already_bound_request_rechecks_ownership_after_waiting_for_clean_close(bool grouped, bool ifMatch)
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new CleanCloseFixture(grouped);
        var reference = new DocumentRef(fixture.Workspace, fixture.Session);
        var filter = new DocumentRevisionFilter();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closingContext = EndpointFilterInvocationContext.Create(new DefaultHttpContext { RequestAborted = ct }, reference);
        var closing = filter.InvokeAsync(closingContext, async _ =>
        {
            entered.TrySetResult();
            await resume.Task.WaitAsync(ct);
            Assert.True(fixture.Workspace.CloseClean(fixture.Session.Id));
            return Results.NoContent();
        }).AsTask();
        Task<object?>? waiting = null;
        bool mutationExecuted = false;
        try
        {
            await TestWorker.WaitForSignalAsync(entered.Task, closing, ct);
            var http = new DefaultHttpContext { RequestAborted = ct };
            if (ifMatch) http.Request.Headers.IfMatch = "\"0\"";
            var invocation = EndpointFilterInvocationContext.Create(http, reference);
            waiting = filter.InvokeAsync(invocation, _ =>
            {
                mutationExecuted = true;
                return ValueTask.FromResult<object?>(fixture.Edit());
            }).AsTask();
            Assert.False(waiting.IsCompleted);
        }
        finally { resume.TrySetResult(); }

        _ = await closing;
        var result = Assert.IsAssignableFrom<IStatusCodeHttpResult>(await waiting!);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.False(mutationExecuted);
        fixture.AssertClosed();
    }
}
