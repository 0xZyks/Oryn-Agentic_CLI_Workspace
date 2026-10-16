using System.Runtime.CompilerServices;
using Oryn.App;

namespace Oryn.TestSupport;

/// <summary>Deterministic renderer fixture. It never loads a model or accesses a network.</summary>
internal sealed class StubInferenceProvider : IInferenceProvider
{
    public InferenceCapabilities Capabilities => new(true, true, false, false);
    public Task<PreparedInference> PrepareAsync(IReadOnlyList<ChatTurn> history, ChatContext context, CancellationToken token)
        => Task.FromResult(new PreparedInference(history, 0, false, 0));
    public async IAsyncEnumerable<InferenceChunk> StreamAsync(PreparedInference request, [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Delay(100, token);
        yield return new("Hello "); yield return new("World!", FinishReason: "stop");
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
