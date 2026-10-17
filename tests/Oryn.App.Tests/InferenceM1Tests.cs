using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class InferenceM1Tests
{
    private sealed class Scratch : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "oryn-m1-tests-" + Guid.NewGuid());
        public Scratch() => Directory.CreateDirectory(Root);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private static ChatPreviewMessage Message(long sequence, string text, ChatMessageStatus status = ChatMessageStatus.Complete)
        => new(sequence % 2 == 1 ? ChatPreviewRole.User : ChatPreviewRole.Oryn, text) { Sequence = sequence, Status = status };

    [Fact]
    public void CompressionPagingAndRestartPreserveEveryMessageAndProjectIdentity()
    {
        using Scratch scratch = new();
        var settings = new WorkspaceSettings { ChatRecentMessages = 4, ChatSegmentBytes = 4096 };
        var source = Enumerable.Range(1, 12).Select(i => Message(i, new string((char)('a' + i), 1800) + " 世界")).ToArray();
        Guid project;
        using (var store = new ProjectChatStore(scratch.Root, settings))
        {
            project = store.ProjectId; store.Save(source);
            Assert.NotEmpty(Directory.GetFiles(store.DataDirectory, "*.gz"));
            Assert.Equal(JsonSerializer.Serialize(source.TakeLast(4)), JsonSerializer.Serialize(store.Load()));
        }
        using (var reopened = new ProjectChatStore(scratch.Root, settings))
        {
            Assert.Equal(project, reopened.ProjectId);
            List<ChatPreviewMessage> all = [.. reopened.Load()];
            while (true)
            {
                var older = reopened.LoadOlder(all[0].Sequence, 4);
                if (older.Count == 0) break;
                all.InsertRange(0, older);
            }
            Assert.Equal(JsonSerializer.Serialize(source), JsonSerializer.Serialize(all));
        }
    }
    [Fact]
    public void MigrationRetainsLegacyBytesAndUsesTheSameIdsOnReopen()
    {
        using Scratch scratch = new(); Directory.CreateDirectory(Path.Combine(scratch.Root, ".oryn"));
        string legacy = Path.Combine(scratch.Root, ".oryn/global-chat.json");
        byte[] bytes = Encoding.UTF8.GetBytes("[{\"Role\":0,\"Text\":\"hello\"},{\"Role\":1,\"Text\":\"世界\"}]");
        File.WriteAllBytes(legacy, bytes);
        Guid[] identities;
        using (var store = new ProjectChatStore(scratch.Root)) identities = store.Load().Select(m => m.Id).ToArray();
        using (var store = new ProjectChatStore(scratch.Root)) Assert.Equal(identities, store.Load().Select(m => m.Id));
        Assert.Equal(bytes, File.ReadAllBytes(legacy));
    }
    [Fact]
    public void PartialJournalTailIsBackedUpAndGeneratingReplyBecomesInterrupted()
    {
        using Scratch scratch = new(); string directory;
        using (var store = new ProjectChatStore(scratch.Root))
        {
            store.Save([Message(1, "question"), Message(2, "partial", ChatMessageStatus.Generating)]); directory = store.DataDirectory;
        }
        File.AppendAllText(Path.Combine(directory, "active.jsonl"), "{truncated");
        using var reopened = new ProjectChatStore(scratch.Root);
        Assert.Equal("partial", reopened.Load()[^1].Text);
        Assert.Equal(ChatMessageStatus.Interrupted, reopened.Load()[^1].Status);
        Assert.Single(Directory.GetFiles(directory, "active.jsonl.recovery-*"));
    }
    [Fact]
    public void InteriorCorruptionIsNeverRewrittenAndASecondWriterIsRejected()
    {
        using Scratch scratch = new(); string directory;
        using (var first = new ProjectChatStore(scratch.Root))
        {
            first.Save([Message(1, "keep")]); directory = first.DataDirectory;
            Assert.Throws<IOException>(() => new ProjectChatStore(scratch.Root));
        }
        string journal = Path.Combine(directory, "active.jsonl"); File.AppendAllText(journal, "{invalid}\n");
        byte[] original = File.ReadAllBytes(journal);
        Assert.ThrowsAny<JsonException>(() => new ProjectChatStore(scratch.Root));
        Assert.Equal(original, File.ReadAllBytes(journal));
    }
    [Fact]
    public void ACorruptCompressedArchiveBlocksFurtherWritesWithoutDeletingIt()
    {
        using Scratch scratch = new(); var settings = new WorkspaceSettings { ChatRecentMessages = 4, ChatSegmentBytes = 4096 };
        using var store = new ProjectChatStore(scratch.Root, settings);
        store.Save(Enumerable.Range(1, 8).Select(i => Message(i, new string('x', 1800))));
        string archive = Directory.GetFiles(store.DataDirectory, "*.gz").Order().First();
        File.WriteAllBytes(archive, [1, 2, 3]);
        Assert.Throws<InvalidDataException>(() => store.LoadOlder(2));
        Assert.Throws<IOException>(() => store.Save([Message(9, "do not overwrite")]));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(archive));
    }
    [Fact]
    public async Task ContextKeepsTheLatestRequestAndWholeTurnsWhileReservingOutput()
    {
        using HttpClient http = new(new FakeServer());
        var profile = new ModelProfile { ModelPath = "fixture", ContextTokens = 4096, MaxOutputTokens = 1024 };
        await using var provider = new LlamaCppProvider(http, new("http://127.0.0.1:18080"), profile);
        ChatTurn[] history = [new("user", new string('a', 1000)), new("assistant", new string('b', 1000)),
            new("user", new string('c', 1000)), new("assistant", new string('d', 1000)), new("user", "latest")];
        var selected = await provider.PrepareAsync(history, ChatContext.Empty, default);
        Assert.True(selected.ExactTokens); Assert.True(selected.ExcludedTurns > 0);
        Assert.Equal("latest", selected.Messages[^1].Content);
        Assert.Equal("system", selected.Messages[0].Role);
        Assert.Equal(0, selected.ExcludedTurns % 2);
        Assert.True(selected.InputTokens + profile.MaxOutputTokens + profile.SafetyTokens <= profile.ContextTokens);
        foreach (var turn in selected.Messages.Skip(1).SkipLast(1)) Assert.Equal(1000, turn.Content.Length);
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.PrepareAsync([new("user", new string('x', 5000))], ChatContext.Empty, default));
    }
    [Fact]
    public async Task TruncatedSseIsAnErrorEvenIfSomeTextWasReceived()
    {
        using HttpClient http = new(new FakeServer { Truncate = true });
        await using var provider = new LlamaCppProvider(http, new("http://127.0.0.1:18080"), new() { ModelPath = "fixture" });
        List<string> parts = [];
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var part in provider.StreamAsync(new([new("user", "hello")], 1, true, 0), default)) parts.Add(part.Text);
        });
        Assert.Contains("partial", parts);
    }
    [Theory]
    [InlineData("gpt-oss-20b-MXFP4.gguf", "Qwen3-8B", "GPT-OSS-20B")]
    [InlineData("Qwen3-8B-Q4_K_M.gguf", "GPT-OSS-20B", "Qwen3-8B")]
    [InlineData("custom.gguf", "GPT-OSS-20B", "famille non identifiée")]
    public async Task IdentityUsesTheLaunchedModelInBothTokenCountingAndGeneration(string file, string oldName, string expected)
    {
        var server = new FakeServer(); using HttpClient http = new(server);
        // Deliberately conflicting catalog ID; the runtime's actual launched file takes priority.
        await using var provider = new LlamaCppProvider(http, new("http://127.0.0.1:18080"),
            new() { CatalogId = "8b-q4" }, "oryn-random-alias", file);
        var prepared = await provider.PrepareAsync([new("user", "Ton modèle ?"), new("assistant", "Je suis " + oldName),
            new("user", "Et maintenant ?")], ChatContext.Empty, default);
        await foreach (var _ in provider.StreamAsync(prepared, default)) { }
        Assert.Contains(expected, server.TemplateSystemPrompt);
        Assert.Equal(prepared.Messages[0].Content, server.TemplateSystemPrompt);
        Assert.Equal(server.TemplateSystemPrompt, server.CompletionSystemPrompt);
        Assert.DoesNotContain("oryn-random-alias", server.CompletionSystemPrompt);
        Assert.Contains("Tu es Oryn", server.CompletionSystemPrompt);
    }
    [Fact]
    public async Task OccupiedPortIsRejectedWithoutStartingOrAdoptingABackend()
    {
        using Scratch scratch = new(); string model = Path.Combine(scratch.Root, "fixture.gguf"); File.WriteAllText(model, "fixture");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var settings = new WorkspaceSettings { InferenceModelPath = model, InferenceEndpoint = $"http://127.0.0.1:{port}" };
            await Assert.ThrowsAsync<IOException>(() => LlamaCppRuntime.StartAsync(scratch.Root, settings, default));
            using TcpClient client = new(); await client.ConnectAsync(IPAddress.Loopback, port);
            using var accepted = await listener.AcceptTcpClientAsync(); Assert.True(accepted.Connected);
        }
        finally { listener.Stop(); }
    }
    [Fact]
    public async Task FastBackendFailureIncludesDrainedDiagnosticsAndMasksTheApiKey()
    {
        if (!OperatingSystem.IsLinux()) return;
        using Scratch scratch = new();
        string model = Path.Combine(scratch.Root, "fixture.gguf"), runner = Path.Combine(scratch.Root, "fail.sh");
        File.WriteAllText(model, "fixture");
        File.WriteAllText(runner, "printf 'backend startup failure: %s\\n' \"$LLAMA_API_KEY\" >&2\nexit 7\n");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var settings = new WorkspaceSettings { InferenceModelPath = model, InferenceRunnerPath = runner,
            PythonExecutable = "/bin/sh", InferenceEndpoint = $"http://127.0.0.1:{port}" };
        var error = await Assert.ThrowsAsync<IOException>(() => LlamaCppRuntime.StartAsync(scratch.Root, settings, default));
        Assert.Contains("code 7", error.Message);
        Assert.Contains("backend startup failure: [masked]", error.Message);
    }
    [Fact]
    public void ChatCopyUsesFullCurrentMessageWithoutChangingTheDraftAndCanCopyErrors()
    {
        using Scratch scratch = new();
        using GraphicalWorkspaceController ui = new(new(scratch.Root), new LocalWorkspaceFiles(scratch.Root, new()), new(new()), Path.Combine(scratch.Root, "keys.json"));
        ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("unsent draft");
        var message = Message(1, "```cs\n// 世界 👋\n" + new string('x', 2000) + "\n```");
        ui.State.Chat.Load([message]);
        ui.CopyChatMessage(message.Id);
        Assert.Equal(message.Text, ui.TakeClipboardText()); Assert.Null(ui.TakeClipboardText());
        Assert.Equal("unsent draft", ui.Editor.Text);
        ui.State.Chat.FailReply("llama-server arrêté\nerror detail"); ui.CopyChatError();
        Assert.Equal(ui.State.Chat.Error, ui.TakeClipboardText());
        ui.CopyChatMessage(Guid.NewGuid()); Assert.Null(ui.TakeClipboardText());
        ui.ClipboardCompleted(false, "unavailable");
        Assert.Contains("unavailable", ui.State.Status);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemoryProfileReachesBothLaunchersAndCudaFailureRetainsAnActionableDiagnostic(bool native)
    {
        if (!OperatingSystem.IsLinux()) return;
        using Scratch scratch = new();
        string model = Path.Combine(scratch.Root, "fixture.gguf"), runner = Path.Combine(scratch.Root, "capture.sh");
        File.WriteAllText(model, "fixture");
        File.WriteAllText(runner, "#!/bin/sh\nprintf 'args:' >&2\nprintf '<%s>' \"$@\" >&2\nprintf '\\nCUDA cudaMalloc failed: out of memory\\n' >&2\nexit 1\n");
        File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var profile = new ModelProfile { ModelPath = model, CpuMoeLayers = 2, FlashAttention = "on" };
        var settings = new WorkspaceSettings { ModelProfiles = new() { ["core"] = profile },
            InferenceRunnerPath = native ? null : runner, LlamaServerPath = native ? runner : null,
            PythonExecutable = "/bin/sh", InferenceEndpoint = $"http://127.0.0.1:{port}" };
        string config = Path.Combine(scratch.Root, "settings.json");
        WorkspaceSettings.Save(config, settings); settings = WorkspaceSettings.Load(config);
        var error = await Assert.ThrowsAsync<IOException>(() => LlamaCppRuntime.StartAsync(scratch.Root, settings, default));
        Assert.Contains(native ? "<--n-cpu-moe><2>" : "<--cpu-moe><2>", error.Message);
        Assert.Contains(native ? "<--flash-attn><on>" : "<--flash><on>", error.Message);
        Assert.Contains("Mémoire GPU insuffisante", error.Message);
        Assert.Contains("cudaMalloc failed: out of memory", error.Message);
        Assert.Contains("code 1", error.Message);
    }
    [Fact]
    public void CudaCoreSuggestionPreservesTheMeasuredHybridPlacementWithoutChangingCpuOnlyProfiles()
    {
        var machine = new HardwareSnapshot("linux-x64", 12, 16L << 30, 10L << 30, 50L << 30,
            [new("RTX 3060", 12L << 30, 11L << 30)], "fixture");
        var suggestions = HardwareProbe.Suggest(machine);
        var core = suggestions.Single(s => s.Model.Id == "gptoss20b-mxfp4").Profile;
        Assert.Equal(2, core.CpuMoeLayers); Assert.Equal("on", core.FlashAttention); Assert.Equal(4096, core.ContextTokens);
        Assert.Equal(0, suggestions.Single(s => s.Model.Id == "8b-q4").Profile.CpuMoeLayers);
        var cpu = HardwareProbe.Suggest(machine with { Gpus = [] }).Single(s => s.Model.Id == "gptoss20b-mxfp4").Profile;
        Assert.Equal(0, cpu.GpuLayers); Assert.Equal(0, cpu.CpuMoeLayers); Assert.Equal("auto", cpu.FlashAttention);
    }
    [Fact]
    public async Task SetupReselectsTheActiveModelAndKeepsCustomTuningThroughSaveAndLoadFailure()
    {
        if (!OperatingSystem.IsLinux()) return;
        using Scratch scratch = new();
        string model = Path.Combine(scratch.Root, "fixture.gguf"), runner = Path.Combine(scratch.Root, "fail.sh");
        string config = Path.Combine(scratch.Root, "custom-settings.json");
        File.WriteAllText(model, "fixture"); File.WriteAllText(runner, "exit 7\n");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var profile = new ModelProfile { CatalogId = "gptoss20b-mxfp4", ModelPath = model, ContextTokens = 8192, CpuMoeLayers = 4, FlashAttention = "on" };
        var settings = new WorkspaceSettings { ModelProfiles = new() { ["core"] = profile }, InferenceRunnerPath = runner,
            PythonExecutable = "/bin/sh", InferenceEndpoint = $"http://127.0.0.1:{port}" };
        using GraphicalWorkspaceController ui = new(new(scratch.Root), new LocalWorkspaceFiles(scratch.Root, settings), new(new()),
            Path.Combine(scratch.Root, "keys.json"), settings: settings, configPath: config);
        var hardware = new HardwareSnapshot("linux-x64", 12, 16L << 30, 10L << 30, 50L << 30,
            [new("RTX 3060", 12L << 30, 11L << 30)], "fixture");
        ui.InferenceSetup.Refresh(hardware, HardwareProbe.Suggest(hardware), profile.CatalogId);
        Assert.Equal(profile.CatalogId, ui.InferenceSetup.Selected!.Model.Id);
        Assert.Equal(profile, ui.SelectedSetupProfile);
        ui.ActivateSetupSelection();
        Assert.Equal(profile, WorkspaceSettings.Load(config).SelectedProfile);
        Assert.Equal(Path.Combine(scratch.Root, "custom-settings.json"), ui.ConfigurationPath);
        for (int i = 0; i < 1000; i++)
        {
            ui.Tick(i);
            if (ui.BackendStatus.StartsWith("Chargement échoué", StringComparison.Ordinal) && ui.InferenceSetup.Status.Contains("code 7", StringComparison.Ordinal)) break;
            await Task.Delay(5);
        }
        Assert.StartsWith("Chargement échoué", ui.BackendStatus);
        Assert.Equal(profile, WorkspaceSettings.Load(config).SelectedProfile);
        Assert.Contains("code 7", ui.InferenceSetup.Status);
    }
    [Fact]
    public void CpuOrUnknownMemoryDoesNotAutomaticallyRecommendTheLargeCoreModel()
    {
        var cpu = new HardwareSnapshot("linux-x64", 4, 16L << 30, 10L << 30, 50L << 30, [], "fixture");
        var suggestions = HardwareProbe.Suggest(cpu);
        Assert.True(suggestions.Single(s => s.Model.Id == "4b-q4").Recommended);
        Assert.False(suggestions.Single(s => s.Model.Id == "gptoss20b-mxfp4").Recommended);
        Assert.DoesNotContain(HardwareProbe.Suggest(cpu with { RamAvailableBytes = null }), s => s.Recommended);
    }
    [Fact]
    public void NoModelPreservesTheDraftAndDoesNotInventAReply()
    {
        using Scratch scratch = new();
        using GraphicalWorkspaceController ui = new(new(scratch.Root), new LocalWorkspaceFiles(scratch.Root, new()), new(new()), Path.Combine(scratch.Root, "keys.json"));
        ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("keep this request"); ui.Key(new("ENTER"), 0);
        Assert.Equal("keep this request", ui.State.Chat.Draft); Assert.Empty(ui.State.Chat.Messages);
        Assert.False(ui.State.Chat.IsThinking); Assert.Contains("modèle", ui.State.Chat.Error);
    }
    [Fact]
    public async Task UncooperativeProviderCannotAppendLateChunksAfterCancellationOrIntoTheNextReply()
    {
        using Scratch scratch = new(); var provider = new LateProvider();
        using GraphicalWorkspaceController ui = new(new(scratch.Root), new LocalWorkspaceFiles(scratch.Root, new()), new(new()),
            Path.Combine(scratch.Root, "keys.json"), inferenceProvider: provider);
        ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("first"); ui.Key(new("ENTER"), 0);
        await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        ui.Key(new("ESC"), 1); provider.Release.TrySetResult();
        await WaitReply(ui);
        Assert.Equal(ChatMessageStatus.Cancelled, ui.State.Chat.Messages[^1].Status);
        Assert.Equal("", ui.State.Chat.Messages[^1].Text);
        ui.Text("second"); ui.Key(new("ENTER"), 2); await WaitReply(ui);
        Assert.Equal("new", ui.State.Chat.Messages[^1].Text);
        Assert.DoesNotContain(ui.State.Chat.Messages, m => m.Text.Contains("late", StringComparison.Ordinal));
    }
    private static async Task WaitReply(GraphicalWorkspaceController ui)
    {
        for (int i = 0; i < 1000; i++)
        { ui.Tick(i); if (!ui.State.Chat.IsThinking) return; await Task.Delay(5); }
        throw new TimeoutException("Injected generation did not complete.");
    }
    private sealed class LateProvider : IInferenceProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public InferenceCapabilities Capabilities => new(true, true, false, false);
        public Task<PreparedInference> PrepareAsync(IReadOnlyList<ChatTurn> history, ChatContext context, CancellationToken token)
            => Task.FromResult(new PreparedInference(history, 0, false, 0));
        public async IAsyncEnumerable<InferenceChunk> StreamAsync(PreparedInference request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            { Entered.TrySetResult(); await Release.Task; yield return new("late"); }
            else yield return new("new", FinishReason: "stop");
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeServer : HttpMessageHandler
    {
        public bool Truncate { get; init; }
        public string TemplateSystemPrompt { get; private set; } = "";
        public string CompletionSystemPrompt { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath == "/apply-template")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                TemplateSystemPrompt = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
                string prompt = string.Join("", body.RootElement.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("content").GetString()));
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { prompt }) };
            }
            if (request.RequestUri.AbsolutePath == "/tokenize")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { tokens = new int[body.RootElement.GetProperty("content").GetString()!.Length] }) };
            }
            using (var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)))
                CompletionSystemPrompt = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
            string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n";
            if (!Truncate) sse += "data: [DONE]\n\n";
            return new(HttpStatusCode.OK) { Content = new StringContent(sse, Encoding.UTF8, "text/event-stream") };
        }
    }
}
