using Oryn.App;
using Oryn.Ui.Sdl;
using SkiaSharp;

/// <summary>Render-only fixture for visual review; no test suite, SDL window, provider or project history.</summary>
internal static class ChatUiPreview
{
    public static int Run(string[] args)
    {
        bool markdown = args.Contains("--markdown-preview");
        string output = Path.GetFullPath(args.Length > 1 ? args[1] : "artifacts/m1/chat-ui-preview");
        Directory.CreateDirectory(output);
        string root = Path.Combine(Path.GetTempPath(), "oryn-chat-preview-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            using GraphicalWorkspaceController ui = new(new(root), new LocalWorkspaceFiles(root, new()), new(new()), Path.Combine(root, "keys.json"));
            ui.Execute(KeyboardAction.FocusChat, 0);
            var prompt = new ChatPreviewMessage(ChatPreviewRole.User,
                "Peux-tu expliquer comment protéger le contexte d'un projet pendant une génération en streaming ? " +
                "Je souhaite conserver les sources, les décisions et les réponses partielles, puis reprendre la discussion après un redémarrage.");
            var reply = new ChatPreviewMessage(ChatPreviewRole.Oryn,
                "On peut conserver un journal local et enregistrer la réponse au fil de sa réception. " +
                "L'annulation interrompt le calcul sans effacer le texte reçu ni le prochain brouillon.\n\n" +
                "Exemple C# :\n```csharp\nusing var cancellation = new CancellationTokenSource();\nawait foreach (var token in GenerateAsync(cancellation.Token))\n    journal.Append(token);\n```\n\n" +
                "Les sources restent traçables. Sélectionne seulement la partie utile avec la souris, puis utilise Ctrl+C.");
            if (markdown)
                reply = reply with { Text = """
                    # Préserver le contexte

                    Une réponse avec du **gras**, de l'*italique*, du ***gras italique***, du ~~texte barré~~ et du `code inline`.
                    L'annulation interrompt le calcul sans effacer le texte reçu ni le prochain brouillon.

                    ## Étapes proposées

                    - Conserver les sources **avec leur provenance**.
                      - Préserver les décisions utilisateur.
                    - [x] Sauvegarder le journal local.
                    - [ ] Vérifier la reprise après redémarrage.

                    1. Préparer la demande.
                    2. Appliquer les budgets.

                    > Un résultat généré reste une hypothèse tant qu'il n'a pas été vérifié.

                    ```csharp
                    using var cancellation = new CancellationTokenSource();
                    await foreach (var token in GenerateAsync(cancellation.Token))
                        journal.Append(token);
                    ```

                    ## Comparaison

                    | Opération | Vérification | État |
                    | --- | --- | --- |
                    | Sauvegarde locale | Relecture et hash | **À tester** |
                    | Annulation | Arrêt du flux | Disponible |

                    Consulter la [documentation du projet](https://example.invalid/oryn).
                    Une image distante reste son texte alternatif : ![Schéma du contexte](https://example.invalid/context.png).

                    ---

                    Les caractères échappés restent visibles : \*littéral\* &amp; Unicode 👩‍💻 é.
                    """ };
            ui.State.Chat.Load([prompt, reply]); ui.Text("Et comment contrôler la taille du contexte ?");
            using WorkspacePainter painter = new(ui);
            if (markdown)
            {
                File.WriteAllText(Path.Combine(output, "markdown-source.md"), reply.Text);
                File.WriteAllText(Path.Combine(output, "markdown-visible.txt"), ChatMarkdown.CopySelection(reply.Text, 0, reply.Text.Length));
                Save(painter, output, "markdown-full.png", 1280, 1900);
                ui.SetChatScroll(1000, 1000);
            }
            Save(painter, output, "chat-1280.png", 1280, 900);
            Save(painter, output, "chat-wide.png", 1920, 1080);
            ui.BeginChatSelection(reply.Id, reply.Text.IndexOf("L'annulation", StringComparison.Ordinal));
            ui.ExtendChatSelection(reply.Id, reply.Text.IndexOf(markdown ? "\n\n## Étapes" : "\n\nExemple", StringComparison.Ordinal)); ui.PointerUp();
            Save(painter, output, "chat-selection.png", 1280, 900);
            Save(painter, output, "chat-selection-hidpi.png", 1280, 900, 2);
            ui.SetChatScroll(1000, 1000);
            Save(painter, output, "chat-compact.png", 630, 660);
            ui.OpenOverlay(WorkspaceOverlay.ElementChat); ui.CurrentChat.Load([prompt, reply]);
            ui.SetChatScroll(1000, 1000);
            Save(painter, output, "chat-element.png", 1280, 900);
            if (markdown)
            {
                ui.CloseOverlay(); ui.SetChatScroll(0, 0); Save(painter, output, "markdown-bottom.png", 1280, 1000);
                ui.State.Chat.Load([prompt]); ui.State.Chat.BeginReply(); ui.State.Chat.AppendReply("```csharp\n// Bloc encore en streaming\nvar count = 42;\n");
                Save(painter, output, "markdown-streaming.png", 900, 660);
            }
            Console.WriteLine("Render-only chat previews: " + output);
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Save(WorkspacePainter painter, string output, string name, int width, int height, int scale = 1)
    {
        using SKBitmap bitmap = new(new SKImageInfo(width * scale, height * scale)); using SKCanvas canvas = new(bitmap);
        painter.Draw(canvas, width, height, scale, scale, animationTime: 0);
        using SKImage image = SKImage.FromBitmap(bitmap); using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(output, name)); data.SaveTo(file);
    }
}
