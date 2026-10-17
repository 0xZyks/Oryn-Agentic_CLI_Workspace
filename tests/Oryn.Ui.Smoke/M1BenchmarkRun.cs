using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Oryn.App;
using Oryn.Ui.Sdl;

/// <summary>Script the prepared public battery through the actual chat controller, store and SDL presentation.</summary>
internal static class M1BenchmarkRun
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record Observation(string Id, string Status, string Error, string ApplicationStatus,
        double? FirstPresentedSeconds, double TotalSeconds, string BackendStatus, bool HistoryRecovered);

    public static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: --m1-benchmark <fresh prepared run directory>");
        string run = Path.GetFullPath(args[1]);
        string metadataPath = Path.Combine(run, "run.json");
        JsonObject metadata = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
        string suiteHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(run, "suite.json")))).ToLowerInvariant();
        if (suiteHash != metadata["suite_sha256"]!.GetValue<string>()) throw new InvalidDataException("Suite snapshot changed.");
        string[] prompts = Directory.GetFiles(Path.Combine(run, "prompts"), "*.txt").Order().ToArray();
        if (prompts.Length == 0 || File.Exists(Path.Combine(run, "execution.json"))) throw new IOException("A fresh prepared run is required.");
        foreach (string prompt in prompts)
        {
            string id = Path.GetFileNameWithoutExtension(prompt);
            if (!Regex.IsMatch(id, "^[A-Z][0-9]{2}$")) throw new InvalidDataException("Unexpected case ID.");
            string answer = Path.Combine(run, "answers", id + ".txt");
            string workspace = Path.Combine(run, "workspaces", id);
            if (File.Exists(answer) && new FileInfo(answer).Length > 0 || Directory.Exists(Path.Combine(workspace, ".oryn")))
                throw new IOException("Existing answers/history preserved; prepare a new run.");
        }
        metadata["measurement_method"] = "Scripted Oryn chat/controller + real SDL presentation, native model and persisted isolated history. First output measured after SDL_RenderPresent; compositor latency unmeasured. Controller input, not native keyboard/clipboard automation.";
        metadata["backend_version"] = InferenceInstallation.Version;
        metadata["model_restarted_per_case"] = true;
        metadata["cache_condition"] = "OS/GPU caches uncontrolled; process starts are not cold-disk claims.";
        metadata["started_at"] = DateTimeOffset.UtcNow.ToString("O");
        File.WriteAllText(metadataPath, metadata.ToJsonString(Json));
        List<Observation> observations = [];
        string label = metadata["model_label"]!.GetValue<string>();
        string condition = metadata["condition"]!.GetValue<string>();
        foreach (string promptFile in prompts)
        {
            string id = Path.GetFileNameWithoutExtension(promptFile);
            string workspace = Path.Combine(run, "workspaces", id);
            string configPath = Path.Combine(workspace, WorkspaceSettings.ProjectFileName);
            WorkspaceSettings settings = WorkspaceSettings.Load(configPath);
            if (settings.SelectedProfile is null) throw new InvalidDataException("A configured model is required.");
            if (observations.Count == 0)
            {
                string model = InferenceInstallation.ModelPath(workspace, settings, settings.SelectedProfile);
                using FileStream modelFile = File.OpenRead(model);
                metadata["model_hash"] = Convert.ToHexString(SHA256.HashData(modelFile)).ToLowerInvariant();
                metadata["model_path"] = model;
                metadata["selected_profile"] = settings.SelectedModelProfile;
                metadata["effective_profile"] = JsonSerializer.SerializeToNode(settings.SelectedProfile);
                File.WriteAllText(metadataPath, metadata.ToJsonString(Json));
            }
            Console.WriteLine($"START {id} / {settings.SelectedModelProfile}");
            string source = File.ReadAllText(promptFile);
            ChatPreviewMessage? answer = null;
            double? first = null; double total = 0;
            string error = "", appStatus = "", backendStatus = "";
            bool submitted = false, completed = false;
            Stopwatch lifetime = Stopwatch.StartNew(), clock = new();
            using (GraphicalWorkspaceController ui = new(new(workspace), new LocalWorkspaceFiles(workspace, settings), new(new()),
                Path.Combine(workspace, "benchmark-keybindings.json"), Path.Combine(workspace, "benchmark-appearance.json"), settings, configPath))
            {
                ui.State.LoadRoot(new LocalWorkspaceFiles(workspace, settings).ListAsync(workspace, default).GetAwaiter().GetResult());
                ui.Execute(KeyboardAction.FocusChat, GraphicalWorkspaceController.Now);
                try
                {
                    using SdlWorkspace window = new(ui, settings.UseColor, settings.UseUnicode, fps: 60);
                    window.Run(host =>
                    {
                        if (!submitted)
                        {
                            if (lifetime.Elapsed.TotalSeconds > 20) throw new TimeoutException("History startup timed out.");
                            if (ui.HistoryLoading) return;
                            if (ui.State.Chat.Messages.Count != 0 || ui.State.Chat.Error.Length > 0)
                                throw new IOException("Scratch history is not empty/usable: " + ui.State.Chat.Error);
                            ui.Text(source); clock.Start(); ui.Key(InputKey.Parse("Enter"), GraphicalWorkspaceController.Now);
                            submitted = true;
                            if (!ui.State.Chat.IsThinking) throw new IOException("Submission rejected: " + ui.State.Chat.Error);
                            return;
                        }
                        answer = ui.State.Chat.Messages.LastOrDefault(m => m.Role == ChatPreviewRole.Oryn);
                        if (first is null && answer is { Text.Length: > 0 }) first = clock.Elapsed.TotalSeconds;
                        if (clock.Elapsed.TotalSeconds > settings.GenerationTimeoutSeconds + 20)
                            throw new TimeoutException("Campaign case exceeded application deadline.");
                        if (ui.State.Chat.IsThinking) return;
                        total = clock.Elapsed.TotalSeconds; error = ui.State.Chat.Error; appStatus = ui.State.Status;
                        backendStatus = ui.BackendStatus; completed = true;
                        host.Capture(Path.Combine(run, id + ".png"));
                        ui.State.Dispatch(WorkspaceCommand.Quit);
                    });
                    if (!completed) throw new OperationCanceledException("Window closed before the case completed.");
                }
                catch (Exception ex)
                {
                    answer = ui.State.Chat.Messages.LastOrDefault(m => m.Role == ChatPreviewRole.Oryn);
                    total = clock.Elapsed.TotalSeconds; error = ex.ToString();
                    appStatus = ui.State.Status; backendStatus = ui.BackendStatus;
                }
                File.WriteAllText(Path.Combine(run, "answers", id + ".txt"), answer?.Text ?? "");
            }
            bool recovered = false;
            try
            {
                using ProjectChatStore reopened = new(workspace, settings);
                var messages = reopened.Load();
                recovered = messages.Count == 2 && messages[0].Text == source.Trim() && answer is not null
                    && messages[1].Id == answer.Id && messages[1].Text == answer.Text && messages[1].Status == answer.Status;
            }
            catch (Exception ex) { error += " History reopen: " + ex.Message; }
            observations.Add(new(id, completed ? answer?.Status.ToString() ?? "Missing" : "HarnessFailed", error, appStatus, first, total, backendStatus, recovered));
            File.WriteAllText(Path.Combine(run, "execution.json"), JsonSerializer.Serialize(observations, Json));
            // Preserve missing metrics as blanks. Status token counts are exact integers; no parsing of the rounded duration.
            const string header = "case_id,model_id,condition,first_visible_seconds,end_to_end_seconds,load_seconds,input_tokens,output_tokens,reasoning_tokens,sampled_gpu_used_mib,sampled_rss_mib,cancel_seconds,notes";
            List<string> rows = [header];
            foreach (string pending in prompts)
            {
                string caseId = Path.GetFileNameWithoutExtension(pending);
                Observation? item = observations.Find(o => o.Id == caseId);
                var tokens = Regex.Match(item?.ApplicationStatus ?? "", @"entrée (?<input>\d+) / sortie (?<output>\d+) tokens");
                rows.Add(string.Join(',', new[] { caseId, label, condition, Number(item?.FirstPresentedSeconds), Number(item?.TotalSeconds), "",
                    tokens.Success ? tokens.Groups["input"].Value : "", tokens.Success ? tokens.Groups["output"].Value : "", "", "", "", "",
                    item is null ? "" : $"{item.Status}; history_recovered={item.HistoryRecovered}; startup included; first frame presented at 60Hz; OS cache uncontrolled; {item.Error}" }.Select(Csv)));
            }
            File.WriteAllLines(Path.Combine(run, "measurements.csv"), rows);
            Console.WriteLine($"DONE {id}: {observations[^1].Status}, first={Number(first)}s total={Number(total)}s history={recovered} {error}");
            if (!completed || answer?.Status != ChatMessageStatus.Complete) break; // Preserve remaining cases when the backend/window fails.
        }
        metadata["finished_at"] = DateTimeOffset.UtcNow.ToString("O");
        File.WriteAllText(metadataPath, metadata.ToJsonString(Json));
        return observations.Count == prompts.Length && observations.All(o => o.Status == "Complete" && o.Error.Length == 0 && o.HistoryRecovered) ? 0 : 1;
    }

    private static string Number(double? value) => value?.ToString("F6", CultureInfo.InvariantCulture) ?? "";
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
