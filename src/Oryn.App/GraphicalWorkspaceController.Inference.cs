using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Oryn.App;

public sealed partial class GraphicalWorkspaceController
{
    private readonly CancellationTokenSource _inferenceLifetime = new();
    private readonly SemaphoreSlim _backendGate = new(1, 1);
    private readonly Channel<(Guid Generation, ChatPreviewState Chat, string Text)> _tokens = Channel.CreateBounded<(Guid, ChatPreviewState, string)>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });
    private WorkspaceSettings? _inferenceSettings;
    private string _configurationPath = "";
    private Task<(ProjectChatStore Store, IReadOnlyList<ChatPreviewMessage> Messages)>? _historyStartup;
    private ProjectChatStore? _chatStore;
    private string? _historyError;
    private LlamaCppRuntime? _runtime;
    private IInferenceProvider? _injectedProvider;
    private string? _runtimeIdentity;
    private Task? _generationTask, _setupTask, _switchTask, _olderTask, _hardwareTask;
    private Guid _generationId;
    private CancellationTokenSource? _generationCancellation, _setupCancellation, _switchCancellation;
    private bool _inferenceDisposed;
    public InferenceSetupState InferenceSetup { get; } = new();
    public bool IncludeSelectedSource { get; private set; }
    public bool HistoryLoading => _historyStartup is not null;
    public string ActiveModelProfile => _inferenceSettings?.SelectedProfile is null ? "no-model" : _inferenceSettings.SelectedModelProfile;
    public string ConfigurationPath => _configurationPath;
    public string BackendStatus { get; private set; } = "Modèle non chargé";
    public bool ModelSwitching => _switchTask is { IsCompleted: false };
    private string? _lastBackendFailure;
    public ModelProfile? SelectedSetupProfile => InferenceSetup.Selected is { } suggestion ? WithSuggestion(suggestion).SelectedProfile : null;

    private void InitializeInference(WorkspaceSettings? settings, string? configPath, IInferenceProvider? provider)
    {
        _injectedProvider = provider;
        if (provider is not null) settings ??= new() { ModelProfiles = new() { ["core"] = new() { ModelPath = "injected-provider" } } };
        settings?.Validate();
        if (settings is { ModelProfiles.Count: 0, SelectedProfile: { } legacy })
            settings = settings with { ModelProfiles = new() { ["core"] = legacy }, SelectedModelProfile = "core" };
        _inferenceSettings = settings;
        _configurationPath = configPath ?? Path.Combine(State.ProjectRoot, WorkspaceSettings.ProjectFileName);
        if (provider is not null) return; // Host-provided sessions own their persistence separately.
        if (settings is null) return; // Controller-only previews/tests do not touch real project storage.
        _historyStartup = Task.Run(() =>
        {
            var store = new ProjectChatStore(State.ProjectRoot, settings);
            try { return (store, store.Load()); }
            catch { store.Dispose(); throw; }
        });
        State.SetStatus(settings.SelectedProfile is null ? "M1 · sans modèle" : "M1 · profil " + settings.SelectedModelProfile + " (chargement à la demande)");
        if (settings.SelectedProfile is null && !settings.InferenceSetupCompleted) OpenInferenceSetup();
    }
    private void TickInference()
    {
        if (_historyStartup is { IsCompleted: true } startup)
        {
            _historyStartup = null;
            if (startup.IsCompletedSuccessfully)
            {
                _chatStore = startup.Result.Store; State.Chat.Load(startup.Result.Messages);
                if (State.Chat.Messages.Any(m => m.Status == ChatMessageStatus.Interrupted))
                    State.Chat.ContextNotice = "Une génération interrompue a été récupérée ; aucun renvoi automatique.";
            }
            else
            {
                _historyError = startup.Exception?.GetBaseException().Message ?? "Historique indisponible.";
                State.Chat.FailReply("Historique préservé, écriture bloquée : " + _historyError);
            }
            Dirty = true;
        }
        for (int i = 0; i < 64 && _tokens.Reader.TryRead(out var item); i++)
            if (item.Generation == _generationId) { item.Chat.AppendReply(item.Text); Dirty = true; }
    }
    private void CancelGeneration() { _generationCancellation?.Cancel(); _switchCancellation?.Cancel(); }

    private void SubmitChat()
    {
        ChatPreviewState chat = CurrentChat;
        if (_generationTask is { IsCompleted: false } || chat.IsThinking)
        { State.SetStatus("Une génération est déjà active ; Échap pour l'annuler."); return; }
        if (_inferenceSettings?.SelectedProfile is null)
        { chat.FailReply("Aucun modèle configuré. Agents → Inference setup / models."); return; }
        if (HistoryLoading || _switchTask is { IsCompleted: false } || InferenceSetup.Busy)
        { State.SetStatus("Chargement en cours ; le brouillon est conservé."); return; }
        if (_injectedProvider is null && ReferenceEquals(chat, State.Chat) && (_historyError is not null || _chatStore is null))
        { chat.FailReply("Envoi bloqué pour préserver l'historique : " + _historyError); return; }
        string prompt = chat.Draft.Trim();
        if (!chat.Send()) return;
        var settings = _inferenceSettings;
        ChatContext context = ChatContext.Empty;
        if (IncludeSelectedSource && State.Preview is { Error: null } source)
        {
            int length = Math.Min(source.Content.Length, 8000);
            string excerpt = source.Content[..length];
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(excerpt)));
            context = new([$"Source : {Path.GetRelativePath(State.ProjectRoot, source.Path)} · SHA256 extrait {hash} · ajouté {DateTimeOffset.UtcNow:O}\n" +
                $"Extrait explicite du preview en mémoire, non revalidé, {length}/{source.Content.Length} caractères :\n" + excerpt]);
        }
        IncludeSelectedSource = false;
        chat.AttachSources(context.Sources);
        chat.BeginReply(); Editor.Set(""); ChatScroll = 0;
        var snapshot = chat.Messages.ToArray();
        var user = snapshot[^2]; var assistant = snapshot[^1];
        var history = BuildHistory(snapshot);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_inferenceLifetime.Token);
        cancellation.CancelAfter(TimeSpan.FromSeconds(settings.GenerationTimeoutSeconds));
        _generationCancellation = cancellation;
        Guid generation = _generationId = Guid.NewGuid();
        _generationTask = Task.Run(async () =>
        {
            StringBuilder reply = new();
            bool saved = false, locked = false;
            string error = "", finish = ""; ChatMessageStatus status = ChatMessageStatus.Complete;
            Stopwatch elapsed = Stopwatch.StartNew(); long checkpoint = 0;
            int? inputTokens = null, outputTokens = null;
            var store = ReferenceEquals(chat, State.Chat) ? _chatStore : null;
            try
            {
                store?.Save(snapshot); saved = true; // No inference if this write fails.
                await _backendGate.WaitAsync(cancellation.Token); locked = true;
                var provider = _injectedProvider ?? (await EnsureRuntimeAsync(settings, cancellation.Token)).Provider;
                var prepared = await provider.PrepareAsync(history, context, cancellation.Token);
                inputTokens = prepared.InputTokens;
                _completed.Enqueue(() =>
                {
                    if (generation != _generationId) return;
                    chat.ContextNotice = $"{prepared.InputTokens} tokens {(prepared.ExactTokens ? "comptés" : "estimés (borne prudente)")} · {prepared.ExcludedTurns} messages chargés exclus ; archives conservées";
                });
                await foreach (var chunk in provider.StreamAsync(prepared, cancellation.Token))
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    inputTokens = chunk.InputTokens ?? inputTokens; outputTokens = chunk.OutputTokens ?? outputTokens;
                    finish = chunk.FinishReason ?? finish;
                    if (reply.Length + chunk.Text.Length > 131072) throw new IOException("Réponse au-delà de la borne de sécurité.");
                    reply.Append(chunk.Text);
                    for (int offset = 0; offset < chunk.Text.Length; offset += 2048)
                        await _tokens.Writer.WriteAsync((generation, chat, chunk.Text.Substring(offset, Math.Min(2048, chunk.Text.Length - offset))), cancellation.Token);
                    if (elapsed.ElapsedMilliseconds - checkpoint >= 1000)
                    {
                        snapshot[^1] = assistant with { Text = reply.ToString() }; store?.Save(snapshot); checkpoint = elapsed.ElapsedMilliseconds;
                    }
                }
                if (finish == "length") { status = ChatMessageStatus.Truncated; error = "Limite de sortie atteinte ; réponse potentiellement incomplète."; }
                if (reply.Length == 0) throw new IOException("Le backend n'a produit aucun texte visible dans le budget disponible.");
            }
            catch (OperationCanceledException)
            {
                status = _inferenceLifetime.IsCancellationRequested ? ChatMessageStatus.Interrupted : ChatMessageStatus.Cancelled;
                error = elapsed.Elapsed.TotalSeconds >= settings.GenerationTimeoutSeconds ? "Délai global de génération dépassé." : "Génération annulée.";
            }
            catch (Exception ex)
            {
                status = ChatMessageStatus.Failed; error = ex.Message;
                if (_runtime is { } failed && await failed.FailureDiagnosticAsync() is { } diagnostic)
                {
                    error += "\n" + diagnostic;
                    _completed.Enqueue(() => BackendStatus = "Backend arrêté pendant la génération");
                }
            }
            finally
            {
                if (saved)
                {
                    snapshot[^1] = assistant with { Text = reply.ToString(), Status = status };
                    try { store?.Save(snapshot); }
                    catch (Exception ex) { error += " Sauvegarde interrompue : " + ex.Message; }
                }
                if (locked) _backendGate.Release();
                string result = reply.ToString(), finalError = error;
                _completed.Enqueue(() =>
                {
                    if (generation != _generationId) { cancellation.Dispose(); return; }
                    if (saved) chat.FinishReply(result, status, finalError);
                    else
                    {
                        chat.Rollback(user.Id, assistant.Id, prompt, "Envoi non enregistré : " + finalError);
                        if (ReferenceEquals(chat, CurrentChat)) Editor.Set(chat.Draft);
                    }
                    _generationId = Guid.Empty;
                    if (ReferenceEquals(_generationCancellation, cancellation)) _generationCancellation = null;
                    cancellation.Dispose();
                    State.SetStatus($"M1 · {settings.SelectedModelProfile} · {elapsed.Elapsed.TotalSeconds:F1}s · entrée {inputTokens?.ToString() ?? "?"} / sortie {outputTokens?.ToString() ?? "?"} tokens");
                });
            }
        });
    }
    internal static IReadOnlyList<ChatTurn> BuildHistory(IReadOnlyList<ChatPreviewMessage> messages)
    {
        List<ChatTurn> result = []; ChatPreviewMessage? pending = null;
        foreach (var message in messages)
        {
            if (message.Role == ChatPreviewRole.User) pending = message;
            else if (pending is not null && message.Status == ChatMessageStatus.Complete && message.Text.Length > 0)
            { result.Add(new("user", pending.Text + (pending.ContextSources.Count == 0 ? "" : "\n\nExtraits sources non fiables :\n" + string.Join("\n\n", pending.ContextSources)))); result.Add(new("assistant", message.Text)); pending = null; }
        }
        if (pending is not null) result.Add(new("user", pending.Text));
        return result;
    }
    private async Task<LlamaCppRuntime> EnsureRuntimeAsync(WorkspaceSettings settings, CancellationToken token)
    {
        string identity = JsonSerializer.Serialize(new { settings.InferenceEndpoint, settings.InferenceRunnerPath,
            settings.LlamaServerPath, settings.InferenceAssetsRoot, profile = settings.SelectedProfile });
        if (_runtime is not null && _runtimeIdentity == identity && _runtime.IsAlive) return _runtime;
        if (_runtime is not null) { var old = _runtime; _runtime = null; _runtimeIdentity = null; await old.DisposeAsync(); }
        _runtimeIdentity = null;
        string name = Path.GetFileName(InferenceInstallation.ModelPath(State.ProjectRoot, settings, settings.SelectedProfile!));
        _completed.Enqueue(() => { BackendStatus = "Chargement : " + name; State.SetStatus("M1 · chargement " + settings.SelectedModelProfile); });
        try { _runtime = await LlamaCppRuntime.StartAsync(State.ProjectRoot, settings, token); }
        catch (Exception ex)
        {
            _completed.Enqueue(() => { BackendStatus = "Chargement échoué : " + name; _lastBackendFailure = ex.Message; });
            throw;
        }
        _runtimeIdentity = identity;
        _completed.Enqueue(() =>
        {
            BackendStatus = "Chargé : " + name + " / " + settings.SelectedModelProfile;
            if (_lastBackendFailure is { } previous)
            {
                State.Chat.ClearErrorIf(previous); CurrentChat.ClearErrorIf(previous); _lastBackendFailure = null;
            }
        });
        return _runtime;
    }
    public void LoadOlderChat()
    {
        if (_chatStore is null || _olderTask is { IsCompleted: false }) return;
        long first = State.Chat.Messages.FirstOrDefault()?.Sequence ?? long.MaxValue;
        _olderTask = Task.Run(() =>
        {
            try
            {
                var messages = _chatStore.LoadOlder(first);
                _completed.Enqueue(() => { State.Chat.Prepend(messages); State.SetStatus(messages.Count == 0 ? "Début de l'historique" : $"{messages.Count} anciens messages chargés"); });
            }
            catch (Exception ex) { _completed.Enqueue(() => { _historyError = ex.Message; State.SetStatus("Archive préservée : " + ex.Message); }); }
        });
    }
    public void OpenInferenceSetup()
    {
        OpenOverlay(WorkspaceOverlay.InferenceSetup);
        if (_hardwareTask is { IsCompleted: false } || _inferenceSettings is null) return;
        var settings = _inferenceSettings;
        _hardwareTask = Task.Run(async () =>
        {
            try
            {
                var hardware = await HardwareProbe.DetectAsync(InferenceInstallation.AssetsRoot(State.ProjectRoot, settings), _inferenceLifetime.Token);
                var suggestions = HardwareProbe.Suggest(hardware);
                _completed.Enqueue(() => { InferenceSetup.Refresh(hardware, suggestions, _inferenceSettings?.SelectedProfile?.CatalogId);
                    InferenceSetup.Status = "Choisir un modèle ; Entrée utilise un modèle installé ou demande confirmation du téléchargement."; });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _completed.Enqueue(() => InferenceSetup.Status = ex.Message); }
        });
    }
    private KeyboardInputResult SetupKey(InputKey key)
    {
        if (key == new InputKey("Q", InputModifiers.Ctrl)) { State.Dispatch(WorkspaceCommand.Quit); return KeyboardInputResult.Consumed; }
        if (key.Name == "ESC")
        {
            if (InferenceSetup.Busy || _switchTask is { IsCompleted: false }) { _setupCancellation?.Cancel(); CancelGeneration(); }
            else CloseOverlay();
        }
        else if (!InferenceSetup.Busy && _switchTask is not { IsCompleted: false })
        {
            if (key.Name is "UP" or "DOWN" or "TAB")
            {
                int direction = key.Name == "UP" ? -1 : 1;
                int count = InferenceSetup.Suggestions.Count;
                if (count > 0) InferenceSetup.Selection = (InferenceSetup.Selection + count + direction) % count;
                InferenceSetup.ConfirmDownload = false;
            }
            else if (key.Name == "ENTER") ActivateSetupSelection();
            else if (key.Name == "T") ProbeSetupSelection();
            else if (key.Name == "N") SkipInferenceSetup();
        }
        Dirty = true; return KeyboardInputResult.Consumed;
    }
    private WorkspaceSettings WithSuggestion(ModelSuggestion suggestion)
    {
        var current = _inferenceSettings ?? new();
        // Reselecting an installed model must not reset explicit memory/context tuning.
        ModelProfile profile = current.ModelProfiles.TryGetValue(suggestion.Slot, out var existing)
            && existing.CatalogId == suggestion.Model.Id ? existing : suggestion.Profile;
        Dictionary<string, ModelProfile> profiles = new(current.ModelProfiles) { [suggestion.Slot] = profile };
        return current with { ModelProfiles = profiles, SelectedModelProfile = suggestion.Slot, InferenceModelPath = null, InferenceSetupCompleted = true };
    }
    public void SelectSetupSuggestion(int index)
    {
        if (InferenceSetup.Busy || _switchTask is { IsCompleted: false }) return;
        InferenceSetup.Selection = Math.Clamp(index, 0, Math.Max(0, InferenceSetup.Suggestions.Count - 1));
        InferenceSetup.ConfirmDownload = false; Dirty = true;
    }
    public void ActivateSetupSelection()
    {
        if (InferenceSetup.Busy || _switchTask is { IsCompleted: false } || InferenceSetup.Selected is not { } suggestion) return;
        var settings = WithSuggestion(suggestion);
        bool modelExists = File.Exists(InferenceInstallation.ModelPath(State.ProjectRoot, settings, settings.SelectedProfile!));
        bool backendExists = InferenceInstallation.HasBackend(State.ProjectRoot, settings);
        if (modelExists && backendExists) { ApplyModelSettings(settings); return; }
        if (Rows < 28) { InferenceSetup.Status = "Agrandissez la fenêtre pour voir source, taille et licence avant confirmation du téléchargement."; return; }
        if (!InferenceSetup.ConfirmDownload)
        { InferenceSetup.ConfirmDownload = true; InferenceSetup.Status = "Confirmer avec Entrée : télécharger le modèle et le backend affichés. Aucun code/chat transmis."; return; }
        InferenceSetup.ConfirmDownload = false; InferenceSetup.Busy = true;
        var cancel = _setupCancellation = CancellationTokenSource.CreateLinkedTokenSource(_inferenceLifetime.Token);
        _setupTask = Task.Run(async () =>
        {
            try
            {
                long need = (modelExists ? 0 : suggestion.Model.Bytes) + (!backendExists && InferenceInstallation.RuntimeKey(suggestion.Gpu) is { } key ? InferenceInstallation.Archives(key).Sum(a => a.Bytes) * 3 : 0);
                if (InferenceSetup.Hardware?.DiskAvailableBytes is { } free && free < need) throw new IOException("Espace disque libre insuffisant pour ce téléchargement/extraction.");
                var progress = new DirectProgress(message => _completed.Enqueue(() => InferenceSetup.Status = message));
                await InferenceInstallation.InstallAsync(State.ProjectRoot, settings, suggestion.Model, suggestion.Gpu, progress, cancel.Token);
                _completed.Enqueue(() => ApplyModelSettings(settings));
            }
            catch (OperationCanceledException) { _completed.Enqueue(() => InferenceSetup.Status = "Installation annulée ; fichiers existants conservés."); }
            catch (Exception ex) { _completed.Enqueue(() => InferenceSetup.Status = ex.Message); }
            finally { _completed.Enqueue(() => { InferenceSetup.Busy = false; if (ReferenceEquals(_setupCancellation, cancel)) _setupCancellation = null; cancel.Dispose(); }); }
        });
    }
    private void ApplyModelSettings(WorkspaceSettings settings)
    {
        try
        {
            WorkspaceSettings.Save(_configurationPath, settings); _inferenceSettings = settings;
            InferenceSetup.Status = "Profil enregistré. T : essai local ; Échap : retour au chat.";
            SelectModelProfile(settings.SelectedModelProfile);
        }
        catch (Exception ex) { InferenceSetup.Status = "Configuration préservée : " + ex.Message; }
    }
    public void SelectModelProfile(string name)
    {
        if (_inferenceSettings is null || !_inferenceSettings.ModelProfiles.ContainsKey(name))
        { State.SetStatus("Profil absent ; ouvrez le setup pour configurer " + name); return; }
        if (_switchTask is { IsCompleted: false }) { State.SetStatus("Basculement déjà en cours."); return; }
        var settings = _inferenceSettings with { SelectedModelProfile = name };
        try { WorkspaceSettings.Save(_configurationPath, settings); }
        catch (Exception ex) { State.SetStatus("Profil non enregistré : " + ex.Message); return; }
        CancelGeneration(); _inferenceSettings = settings;
        var cancel = _switchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_inferenceLifetime.Token);
        _switchTask = Task.Run(async () =>
        {
            try
            {
                await _backendGate.WaitAsync(cancel.Token);
                try { await EnsureRuntimeAsync(settings, cancel.Token); }
                finally { _backendGate.Release(); }
                _completed.Enqueue(() => { State.SetStatus("M1 · profil " + name + " prêt"); InferenceSetup.Status = "Profil prêt. T : essai local."; });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _completed.Enqueue(() => { State.SetStatus("Chargement : " + ex.Message); InferenceSetup.Status = ex.Message; }); }
            finally { _completed.Enqueue(() => { if (ReferenceEquals(_switchCancellation, cancel)) _switchCancellation = null; cancel.Dispose(); }); }
        });
    }
    public void ProbeSetupSelection()
    {
        if (InferenceSetup.Busy || _switchTask is { IsCompleted: false } || InferenceSetup.Selected is not { } suggestion) return;
        var settings = WithSuggestion(suggestion);
        if (!File.Exists(InferenceInstallation.ModelPath(State.ProjectRoot, settings, settings.SelectedProfile!)))
        { InferenceSetup.Status = "Installez le modèle avant l'essai local."; return; }
        InferenceSetup.Busy = true;
        var cancel = _setupCancellation = CancellationTokenSource.CreateLinkedTokenSource(_inferenceLifetime.Token);
        cancel.CancelAfter(TimeSpan.FromSeconds(settings.StartupTimeoutSeconds + 60));
        _setupTask = Task.Run(async () =>
        {
            bool locked = false;
            try
            {
                await _backendGate.WaitAsync(cancel.Token); locked = true;
                var watch = Stopwatch.StartNew();
                var runtime = await EnsureRuntimeAsync(settings, cancel.Token);
                double ready = watch.Elapsed.TotalSeconds;
                var request = await runtime.Provider.PrepareAsync([new("user", "Réponds uniquement OK.")], ChatContext.Empty, cancel.Token);
                double? first = null; int? output = null;
                await foreach (var chunk in runtime.Provider.StreamAsync(request, cancel.Token))
                { if (chunk.Text.Length > 0) first ??= watch.Elapsed.TotalSeconds; output = chunk.OutputTokens ?? output; }
                if (first is null) throw new IOException("Aucun texte visible pendant l'essai ; ne pas considérer ce profil comme validé.");
                double elapsed = watch.Elapsed.TotalSeconds;
                AtomicFile.Write(Path.Combine(State.ProjectRoot, ".oryn", "inference-probe.json"), JsonSerializer.SerializeToUtf8Bytes(new {
                    timestamp = DateTimeOffset.UtcNow, profile = settings.SelectedProfile, hardware = InferenceSetup.Hardware,
                    readiness_seconds = ready, first_visible_seconds = first, total_seconds = elapsed, output_tokens = output,
                    note = "Un essai local ; réutilisation du modèle possible. Ce n'est pas une mesure de qualité générale ou de mémoire maximale." }));
                _completed.Enqueue(() => InferenceSetup.Status = $"Essai : prêt {ready:F1}s · premier texte {first:F1}s · total {elapsed:F1}s · sortie {output?.ToString() ?? "?"} tokens");
            }
            catch (OperationCanceledException) { _completed.Enqueue(() => InferenceSetup.Status = "Essai local annulé."); }
            catch (Exception ex) { _completed.Enqueue(() => InferenceSetup.Status = ex.Message); }
            finally { if (locked) _backendGate.Release(); _completed.Enqueue(() => { InferenceSetup.Busy = false; if (ReferenceEquals(_setupCancellation, cancel)) _setupCancellation = null; cancel.Dispose(); }); }
        });
    }
    public void SkipInferenceSetup()
    {
        if (InferenceSetup.Busy || _inferenceSettings is null) return;
        try
        {
            var next = _inferenceSettings with { InferenceSetupCompleted = true };
            WorkspaceSettings.Save(_configurationPath, next); _inferenceSettings = next; CloseOverlay();
        }
        catch (Exception ex) { InferenceSetup.Status = ex.Message; }
    }
    private sealed class DirectProgress(Action<string> report) : IProgress<string>
    { public void Report(string value) => report(value); }
    private void StopInference()
    {
        if (_inferenceDisposed) return;
        _inferenceDisposed = true; _inferenceLifetime.Cancel(); _generationCancellation?.Cancel(); _setupCancellation?.Cancel(); _switchCancellation?.Cancel();
        try
        {
            Task.WhenAll(new[] { _generationTask, _setupTask, _switchTask, _olderTask, _hardwareTask }.OfType<Task>()).WaitAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult();
            if (_historyStartup is { } startup) { var result = startup.GetAwaiter().GetResult(); result.Store.Dispose(); }
        }
        catch (Exception ex) { State.SetStatus("Fermeture inférence : " + ex.Message); }
        finally
        {
            try { _runtime?.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(7)).GetAwaiter().GetResult(); }
            catch (Exception ex) { State.SetStatus("Arrêt backend : " + ex.Message); }
            _injectedProvider?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _chatStore?.Dispose();
            if (new[] { _generationTask, _setupTask, _switchTask, _olderTask, _hardwareTask }.OfType<Task>().All(t => t.IsCompleted))
            {
                _generationCancellation?.Dispose(); _setupCancellation?.Dispose(); _switchCancellation?.Dispose();
                _inferenceLifetime.Dispose(); _backendGate.Dispose();
            }
        }
    }
}
