using System.IO;

namespace ProjectHub.Worker;

/// <summary>
/// Installs one reusable runtime UI event/data-binding surface for generated
/// screens. The installation is additive and never overwrites a user file.
/// </summary>
internal static class UnityVisualFoundryRuntime
{
    internal const string RelativeSourcePath =
        "Assets/ProjectHub/VisualFoundry/Runtime/VisualFoundryBridge.cs";

    internal sealed record InstallResult(bool Available, bool Changed, string Detail);

    internal static InstallResult Install(string projectRoot)
    {
        try
        {
            var project = Path.GetFullPath(projectRoot);
            var path = Path.GetFullPath(Path.Combine(project,
                RelativeSourcePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(project.TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return new(false, false, "VISUAL_FOUNDRY_RUNTIME_SCOPE_INVALID");
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path);
                return string.Equals(existing, Source, StringComparison.Ordinal)
                    ? new(true, false, "VISUAL_FOUNDRY_RUNTIME_PRESENT")
                    : new(false, false,
                        "VISUAL_FOUNDRY_RUNTIME_CONFLICT: existing user file preserved " +
                        RelativeSourcePath);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Source, ProjectHubJson.Utf8NoBom);
            return new(true, true, "VISUAL_FOUNDRY_RUNTIME_INSTALLED: " +
                RelativeSourcePath + "; Unity recompile still required");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return new(false, false, "VISUAL_FOUNDRY_RUNTIME_UNVERIFIED: " + ex.Message);
        }
    }

    // This compiles inside the target Unity game, NOT the WPF Worker.
    // No scene/gameplay scripts or user prefabs are modified.
    internal static string Source => """
        using System;
        using System.Collections.Generic;
        using UnityEngine;
        using UnityEngine.SceneManagement;
        using UnityEngine.UI;

        namespace ProjectHub.VisualFoundry
        {
            /// <summary>
            /// Runtime presentation adapter for Visual Foundry's prefab library.
            /// Consumers subscribe to ActionPressed and feed real game data via
            /// ApplyQuiz/ApplyResults/ApplyMenu. No game rules are invented.
            /// </summary>
            public static class VisualFoundryBridge
            {
                public static event Action<string, int> ActionPressed;
                private static readonly HashSet<int> WiredButtons = new HashSet<int>();
                private static bool started;

                [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
                private static void OnRuntimeLoaded()
                {
                    if (started) return;
                    started = true;
                    SceneManager.sceneLoaded += OnSceneLoaded;
                    var host = new GameObject("VisualFoundry Runtime Binding");
                    UnityEngine.Object.DontDestroyOnLoad(host);
                    host.AddComponent<VisualFoundryWatcher>();
                    BindAll();
                }

                private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
                {
                    WiredButtons.Clear();
                    BindAll();
                }

                /// <summary>Public for dynamically instantiated screens.</summary>
                public static void BindAll()
                {
                    foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        if (!canvas.gameObject.name.StartsWith("VF ", StringComparison.Ordinal))
                            continue;
                        foreach (var button in canvas.GetComponentsInChildren<Button>(true))
                        {
                            var id = button.GetInstanceID();
                            if (!WiredButtons.Add(id)) continue;
                            var screen = ScreenName(canvas.gameObject.name);
                            var actionName = button.gameObject.name;
                            if (!actionName.StartsWith("Action ", StringComparison.Ordinal))
                                continue;
                            if (!int.TryParse(actionName.Substring("Action ".Length), out var index))
                                continue;
                            var capturedScreen = screen;
                            var capturedIndex = index;
                            button.onClick.AddListener(() =>
                                ActionPressed?.Invoke(capturedScreen, capturedIndex));
                        }
                    }
                }

                private static string ScreenName(string title)
                {
                    if (title.Contains(" Quiz")) return "Quiz";
                    if (title.Contains(" Results")) return "Results";
                    return "Menu";
                }

                private static Canvas Find(string screen)
                {
                    foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None))
                        if (canvas.gameObject.name.StartsWith("VF ", StringComparison.Ordinal) &&
                            ScreenName(canvas.gameObject.name) == screen)
                            return canvas;
                    return null;
                }

                private static bool Set(Canvas canvas, string node, string value)
                {
                    var field = canvas.transform.Find(node);
                    if (field == null) return false;
                    var label = field.GetComponentInChildren<Text>(true);
                    if (label == null) return false;
                    label.text = value ?? "";
                    return true;
                }

                /// <summary>Returns false if no compatible screen is active.</summary>
                public static bool ApplyQuiz(string question, string[] choices,
                    int number, int total)
                {
                    var view = Find("Quiz");
                    if (view == null || choices == null || choices.Length != 4)
                        return false;
                    Set(view, "Secondary heading", "QUESTION " + number + " / " + total);
                    Set(view, "Content lead", question);
                    for (int i = 0; i < 4; i++)
                        Set(view, "Action " + (i + 1), choices[i]);
                    BindAll();
                    return true;
                }

                public static bool ApplyResults(string title, string summary)
                {
                    var view = Find("Results");
                    if (view == null) return false;
                    Set(view, "Heading", title);
                    Set(view, "Content lead", summary);
                    BindAll();
                    return true;
                }

                public static bool ApplyMenu(string title, string subtitle)
                {
                    var view = Find("Menu");
                    if (view == null) return false;
                    Set(view, "Heading", title);
                    Set(view, "Content lead", subtitle);
                    BindAll();
                    return true;
                }

                public static bool ShowScreen(string screen)
                {
                    var target = Find(screen);
                    if (target == null) return false;
                    foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None))
                        if (canvas.gameObject.name.StartsWith("VF ", StringComparison.Ordinal))
                            canvas.gameObject.SetActive(canvas == target);
                    BindAll();
                    return true;
                }

                internal sealed class VisualFoundryWatcher : MonoBehaviour
                {
                    private float nextScan;
                    private void Update()
                    {
                        if (Time.unscaledTime < nextScan) return;
                        nextScan = Time.unscaledTime + 1.5f;
                        BindAll();
                    }
                }
            }
        }
        """;
}
