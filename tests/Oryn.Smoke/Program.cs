using System.Diagnostics;
using System.Text.Json;
using Oryn.App;

string root = Path.Combine(Path.GetTempPath(), "oryn-smoke-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    string child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
    await File.WriteAllTextAsync(Path.Combine(child, "example.txt"), "Oryn smoke test");
    StartupOptions options = StartupOptions.Parse([], root);
    Require(options.ProjectRoot == root, "Default project must be the caller's working directory.");
    LocalWorkspaceFiles files = new(root, new());
    WorkspaceState state = new(root);
    state.LoadRoot(await files.ListAsync(root, default));
    Require(state.Rows.Count == 1, "Root should contain one unloaded folder.");
    state.Apply((await state.LoadSelectionAsync(files, default))!);
    Require(state.Rows.Count == 2, "Folder should expand lazily.");
    state.Select(1);
    state.Apply((await state.LoadSelectionAsync(files, default))!);
    Require(state.Preview?.Content == "Oryn smoke test", "File should open as a read-only preview.");
    state.Dispatch(WorkspaceCommand.NextPanel);
    Require(state.Focus == WorkspacePanel.Explorer, "Focus should cycle through the two startup panels.");
    Require(WorkspaceLayout.Calculate(60, 20, state.Focus).Compact, "Narrow terminal should use switchable panels.");
    if (args.Length == 1)
    {
        string executable = Path.GetFullPath(args[0]);
        // macOS may expose its temporary folder through /var -> /private/var.
        // Compare the physical caller directory, as returned by GetCurrentDirectory.
        string previousDirectory = Directory.GetCurrentDirectory();
        string callerDirectory;
        try { Directory.SetCurrentDirectory(child); callerDirectory = Directory.GetCurrentDirectory(); }
        finally { Directory.SetCurrentDirectory(previousDirectory); }
        ProcessStartInfo start = new(executable) { WorkingDirectory = child, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--check");
        using Process process = Process.Start(start)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Require(process.ExitCode == 0, "CLI check should succeed: " + error);
        using JsonDocument json = JsonDocument.Parse(output);
        Require(json.RootElement.GetProperty("projectRoot").GetString() == callerDirectory,
            "Executable must open the launch directory, not its installation directory.");
        Console.WriteLine("PASS: executable launched from another directory without a path argument.");
    }
    Console.WriteLine("PASS: project root, lazy explorer, preview, focus and compact layout.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex.Message);
    return 1;
}
finally { Directory.Delete(root, recursive: true); }

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
