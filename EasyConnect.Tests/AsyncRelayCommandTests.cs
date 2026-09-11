using EasyConnect.Helpers;
using Xunit;

namespace EasyConnect.Tests;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task CommandPreventsConcurrentExecutionAndReenablesAfterCompletion()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var command = new AsyncRelayCommand(() =>
        {
            calls++;
            return completion.Task;
        }, _ => { });

        var running = command.ExecuteAsync();
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(1, calls);

        completion.SetResult();
        await running;
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task ICommandEntryPointObservesAsynchronousErrorsAndEnablesRetry()
    {
        var failure = new InvalidOperationException("Device API unavailable");
        var handled = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(async () =>
        {
            await Task.Yield();
            throw failure;
        }, exception => handled.TrySetResult(exception));

        command.Execute(null);
        var observed = await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(failure, observed);
        Assert.True(command.CanExecute(null));
    }
}
