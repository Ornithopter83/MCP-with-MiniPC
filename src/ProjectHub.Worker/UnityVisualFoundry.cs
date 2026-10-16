using System.IO;
using System.Text.Json;

namespace ProjectHub.Worker;

/// <summary>
/// Deterministic, model-independent premium UI kit generator. DESIGN owns
/// visual integration; Worker provides reusable, non-destructive Unity assets.
/// </summary>
internal static class UnityVisualFoundry
{
    internal sealed record Style(
        string Id, string Name, string Back, string Mid, string Panel,
        string Accent, string Secondary, string Text, string Muted,
        string Signature);

    internal sealed record Plan(
        string ProjectRoot, string Directory, string ScriptPath, string ManifestPath,
        string AssetDirectory, Style Theme)
    {
        internal string AgentGuidance =>
            "VISUAL_FOUNDRY: " + Theme.Name + "\n" +
            "프리미엄 화면 킷 경로: " + AssetDirectory + "\n" +
            "기본 화면: Menu.prefab, Quiz.prefab, Results.prefab, Preview.unity.\n" +
            "생성된 샘플 텍스트와 버튼은 게임 로직이 아니다. 기존 게임 데이터·" +
            "입력 이벤트에 맞게 실제 씬/프리팹에 통합하고, 외부 에셋·" +
            "기존 UI를 덮어쓰지 마라. 필요하면 킷을 개조하되 이미지와 " +
            "게임플레이를 별도로 검증하라. 원본 생성 명세: " + ManifestPath;
    }

    internal sealed record Result(bool Success, string Detail, string? AssetDirectory);

    private static readonly Style[] Styles =
    {
        new("neon", "Neon Command", "#07101F", "#132D49", "#102238",
            "#18D5F7", "#FFAC5B", "#ECF8FF", "#A4BCCF", "SYSTEM / ARCADE"),
        new("arcane", "Arcane Atelier", "#160F20", "#342334", "#2B1F2F",
            "#DDB77B", "#A58CF5", "#F7EADA", "#BEB0C0", "THE ARCHIVE"),
        new("luxury", "Noir Edition", "#090D13", "#22252A", "#191C23",
            "#D4B178", "#83A8AE", "#F2ECE2", "#B3B6B9", "PRIVATE EDITION"),
        new("playful", "Nova Pop", "#171A45", "#383A88", "#292B73",
            "#FE7FCB", "#59E7DB", "#FFFFFF", "#CCD3FF", "PLAY / DISCOVER")
    };

    internal static bool IsVisualScreenRequest(string instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions)) return false;
        if (instructions.Contains("NO_VISUAL_FOUNDRY", StringComparison.OrdinalIgnoreCase))
            return false;
        return new[] { "UI", "화면", "게임", "메뉴", "HUD", "퀴즈", "인터페이스",
            "레이아웃", "디자인", "시각", "그래픽", "미술", "scene", "visual", "screen", "menu", "quiz",
            "프리미엄", "고급", "화려" }
            .Any(term => instructions.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    internal static Style Select(string instructions)
    {
        var text = instructions ?? string.Empty;
        if (ContainsAny(text, "판타지", "마법", "중세", "RPG", "fantasy", "medieval", "arcane"))
            return Styles[1];
        if (ContainsAny(text, "미니멀", "럭셔리", "고급스러운", "절제", "luxury", "minimal", "noir"))
            return Styles[2];
        if (ContainsAny(text, "캐주얼", "귀여", "키즈", "어린이", "casual", "cute", "colorful"))
            return Styles[3];
        return Styles[0]; // bold, cinematic default without extra model calls
    }

    private static bool ContainsAny(string text, params string[] words) =>
        words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));

    internal static Plan Prepare(string root, string jobId, string milestoneId,
        string instructions)
    {
        var style = Select(instructions);
        var safeKey = SafePart(jobId) + "_" + SafePart(milestoneId) + "_" + style.Id;
        var folder = Path.Combine(Path.GetFullPath(root), "temp", "ProjectHub",
            SafePart(jobId), "visual-foundry", SafePart(milestoneId));
        Directory.CreateDirectory(folder);
        var relativeAssets = "Assets/ProjectHub/VisualFoundry/" + safeKey;
        var script = Path.Combine(folder, "build-premium-kit.cs");
        var manifest = Path.Combine(folder, "design-dna.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            version = 1,
            source = "UnityWorker.VisualFoundry",
            style = style,
            output = relativeAssets,
            screens = new[] { "Menu", "Quiz", "Results" },
            viewport = new { width = 1920, height = 1080 },
            instructions,
            note = "Presentation-only sample; DESIGN must bind actual gameplay."
        }, new JsonSerializerOptions { WriteIndented = true }), ProjectHubJson.Utf8NoBom);
        File.WriteAllText(script, BuildEditorExpression(relativeAssets, style),
            ProjectHubJson.Utf8NoBom);
        return new(Path.GetFullPath(root), folder, script, manifest, relativeAssets, style);
    }

    private static string SafePart(string s)
    {
        var value = new string((s ?? string.Empty).Take(48)
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(value.Trim('_')) ? "visual" : value;
    }

    internal static string BuildEditorExpression(string relativeAssets, Style style)
    {
        // The script is evaluated by Unity CLI in the project's existing Editor.
        // It creates isolated prefabs, a preview scene and procedural visual
        // materials; never opens the preview over the user's active scene.
        const string script = """
            var vfOutput = __OUTPUT__;
            var vfStyle = __STYLE__;
            var vfBack = __BACK__;
            var vfMid = __MID__;
            var vfPanel = __PANEL__;
            var vfAccent = __ACCENT__;
            var vfSecondary = __SECONDARY__;
            var vfText = __TEXT__;
            var vfMuted = __MUTED__;
            var vfSignature = __SIGNATURE__;
            System.Func<string,UnityEngine.Color> vfColor = h => {
                UnityEngine.Color c;
                if (!UnityEngine.ColorUtility.TryParseHtmlString(h, out c))
                    throw new System.InvalidOperationException("VISUAL_FOUNDRY_BAD_COLOR:" + h);
                return c;
            };
            if (UnityEditor.EditorApplication.isPlaying)
                throw new System.InvalidOperationException("VISUAL_FOUNDRY_REQUIRES_EDIT_MODE");
            var vfParts = vfOutput.Split('/');
            var vfParent = "Assets";
            for (int i = 1; i < vfParts.Length; i++) {
                var vfNext = vfParent + "/" + vfParts[i];
                if (!UnityEditor.AssetDatabase.IsValidFolder(vfNext))
                    UnityEditor.AssetDatabase.CreateFolder(vfParent, vfParts[i]);
                vfParent = vfNext;
            }
            var vfBackTex = new UnityEngine.Texture2D(512, 288,
                UnityEngine.TextureFormat.RGBA32, false);
            vfBackTex.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            var vfPixels = new UnityEngine.Color[512 * 288];
            var vfRng = new System.Random(4217);
            for (int y = 0; y < 288; y++)
                for (int x = 0; x < 512; x++) {
                    float fx = x / 511f, fy = y / 287f;
                    float vignette = UnityEngine.Mathf.Pow(
                        UnityEngine.Mathf.Clamp01(1f - UnityEngine.Mathf.Abs(fx - .51f) * .6f
                        - UnityEngine.Mathf.Abs(fy - .52f) * .3f), 1.5f);
                    var c = UnityEngine.Color.Lerp(vfColor(vfBack), vfColor(vfMid),
                        UnityEngine.Mathf.Clamp01((fx * .35f + fy * .5f) * vignette));
                    float diagonal = UnityEngine.Mathf.Abs(
                        UnityEngine.Mathf.Repeat(x + y * .6f, 43f) - 21.5f);
                    if (diagonal < .55f)
                        c = UnityEngine.Color.Lerp(c, vfColor(vfAccent), .07f);
                    if (vfRng.NextDouble() < .002)
                        c = UnityEngine.Color.Lerp(c, vfColor(vfAccent), .27f);
                    c.a = 1f; vfPixels[y * 512 + x] = c;
                }
            vfBackTex.SetPixels(vfPixels);
            vfBackTex.Apply();
            var vfBackPath = vfOutput + "/Backdrop.png";
            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(UnityEngine.Application.dataPath,
                    vfBackPath.Substring("Assets/".Length).Replace('/',
                        System.IO.Path.DirectorySeparatorChar)),
                vfBackTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(vfBackTex);
            UnityEditor.AssetDatabase.ImportAsset(vfBackPath,
                UnityEditor.ImportAssetOptions.ForceSynchronousImport);
            var vfImporter = UnityEditor.AssetImporter.GetAtPath(vfBackPath)
                as UnityEditor.TextureImporter;
            if (vfImporter == null)
                throw new System.InvalidOperationException("VISUAL_FOUNDRY_SPRITE_IMPORT_FAILED");
            vfImporter.textureType = UnityEditor.TextureImporterType.Sprite;
            vfImporter.mipmapEnabled = false;
            vfImporter.SaveAndReimport();
            var vfSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(
                vfBackPath);
            if (vfSprite == null)
                throw new System.InvalidOperationException("VISUAL_FOUNDRY_SPRITE_MISSING");
            UnityEngine.Font vfFont = null;
            try {
                vfFont = UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>(
                    "LegacyRuntime.ttf");
            } catch (System.Exception) {}
            if (vfFont == null) {
                try {
                    vfFont = UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("Arial.ttf");
                } catch (System.Exception) {}
            }
            if (vfFont == null)
                throw new System.InvalidOperationException("VISUAL_FOUNDRY_FONT_UNAVAILABLE");
            System.Func<string,UnityEngine.Transform,UnityEngine.Vector2,
                UnityEngine.Vector2,UnityEngine.Color,UnityEngine.RectTransform> vfPlate =
                (name, parent, anchor, size, tint) => {
                    var go = new UnityEngine.GameObject(name,
                        typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.Image));
                    go.transform.SetParent(parent, false);
                    var rect = go.GetComponent<UnityEngine.RectTransform>();
                    rect.anchorMin = new UnityEngine.Vector2(.5f,.5f);
                    rect.anchorMax = new UnityEngine.Vector2(.5f,.5f);
                    rect.anchoredPosition = anchor;
                    rect.sizeDelta = size;
                    var image = go.GetComponent<UnityEngine.UI.Image>();
                    image.color = tint;
                    image.raycastTarget = false;
                    return rect;
                };
            System.Action<UnityEngine.RectTransform,string,int,UnityEngine.TextAnchor,
                UnityEngine.Color> vfLabel = (panel, value, size, align, tint) => {
                    var child = new UnityEngine.GameObject("Label",
                        typeof(UnityEngine.RectTransform), typeof(UnityEngine.UI.Text));
                    child.transform.SetParent(panel,false);
                    var rect = child.GetComponent<UnityEngine.RectTransform>();
                    rect.anchorMin = UnityEngine.Vector2.zero;
                    rect.anchorMax = UnityEngine.Vector2.one;
                    rect.offsetMin = new UnityEngine.Vector2(16f,4f);
                    rect.offsetMax = new UnityEngine.Vector2(-16f,-4f);
                    var label = child.GetComponent<UnityEngine.UI.Text>();
                    label.font = vfFont; label.fontSize = size;
                    label.alignment = align; label.color = tint;
                    label.text = value; label.raycastTarget = false;
                    label.resizeTextForBestFit = true;
                    label.resizeTextMinSize = System.Math.Max(12, size - 6);
                    label.resizeTextMaxSize = size;
                };
            var vfScreens = new [] { "Menu", "Quiz", "Results" };
            var vfPrefabPaths = new System.Collections.Generic.List<string>();
            foreach (var vfScreen in vfScreens) {
                var vfRoot = new UnityEngine.GameObject("VF " + vfStyle + " " + vfScreen,
                    typeof(UnityEngine.RectTransform), typeof(UnityEngine.Canvas),
                    typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster),
                    typeof(UnityEngine.CanvasGroup));
                var vfCanvas = vfRoot.GetComponent<UnityEngine.Canvas>();
                vfCanvas.renderMode = UnityEngine.RenderMode.ScreenSpaceOverlay;
                vfCanvas.sortingOrder = 20;
                var vfScaler = vfRoot.GetComponent<UnityEngine.UI.CanvasScaler>();
                vfScaler.uiScaleMode =
                    UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                vfScaler.referenceResolution = new UnityEngine.Vector2(1920f,1080f);
                vfScaler.screenMatchMode =
                    UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                vfScaler.matchWidthOrHeight = .5f;
                var vfRootRect = vfRoot.GetComponent<UnityEngine.RectTransform>();
                vfRootRect.anchorMin = UnityEngine.Vector2.zero;
                vfRootRect.anchorMax = UnityEngine.Vector2.one;
                vfRootRect.offsetMin = UnityEngine.Vector2.zero;
                vfRootRect.offsetMax = UnityEngine.Vector2.zero;
                var bg = vfPlate("Procedural cosmic backdrop",vfRoot.transform,
                    UnityEngine.Vector2.zero,new UnityEngine.Vector2(1920,1080),
                    UnityEngine.Color.white);
                bg.anchorMin=UnityEngine.Vector2.zero; bg.anchorMax=UnityEngine.Vector2.one;
                bg.offsetMin=UnityEngine.Vector2.zero; bg.offsetMax=UnityEngine.Vector2.zero;
                bg.GetComponent<UnityEngine.UI.Image>().sprite = vfSprite;
                var top = vfPlate("Top neon tracing",vfRoot.transform,
                    new UnityEngine.Vector2(0,510),new UnityEngine.Vector2(1720,3),
                    vfColor(vfAccent));
                var subTop = vfPlate("Top sheen",vfRoot.transform,
                    new UnityEngine.Vector2(0,494),new UnityEngine.Vector2(1720,1),
                    vfColor(vfAccent)*.45f);
                var sidebar = vfPlate("Side accent",vfRoot.transform,
                    new UnityEngine.Vector2(-830,0),new UnityEngine.Vector2(5,740),
                    vfColor(vfSecondary));
                var ornament = vfPlate("Upper corner glow",vfRoot.transform,
                    new UnityEngine.Vector2(756,429),new UnityEngine.Vector2(200,4),
                    vfColor(vfSecondary));
                var brand = vfPlate("Brand",vfRoot.transform,
                    new UnityEngine.Vector2(-615,432),new UnityEngine.Vector2(460,72),
                    new UnityEngine.Color(0,0,0,0));
                vfLabel(brand,vfSignature + "   //   " + vfStyle.ToUpperInvariant(),24,
                    UnityEngine.TextAnchor.MiddleLeft,vfColor(vfAccent));
                var frame = vfPlate("Main glass body",vfRoot.transform,
                    new UnityEngine.Vector2(0,-12),new UnityEngine.Vector2(1510,780),
                    vfColor(vfPanel));
                var frameOutline = frame.gameObject.AddComponent<UnityEngine.UI.Outline>();
                frameOutline.effectColor = vfColor(vfAccent) * .27f;
                frameOutline.effectDistance = new UnityEngine.Vector2(2,-2);
                var hdr = vfPlate("Heading",vfRoot.transform,
                    new UnityEngine.Vector2(0,280),new UnityEngine.Vector2(1360,122),
                    new UnityEngine.Color(0,0,0,0));
                var title = vfScreen == "Menu" ? "MISSION CONTROL" :
                    vfScreen == "Quiz" ? "KNOWLEDGE PROTOCOL" : "MISSION COMPLETE";
                vfLabel(hdr,title,66,UnityEngine.TextAnchor.MiddleCenter,vfColor(vfText));
                var strap = vfPlate("Secondary heading",vfRoot.transform,
                    new UnityEngine.Vector2(0,202),new UnityEngine.Vector2(1160,54),
                    new UnityEngine.Color(0,0,0,0));
                vfLabel(strap,vfScreen == "Menu" ? "Select your next operation" :
                    vfScreen == "Quiz" ? "QUESTION 01  /  10" :
                    "PERFORMANCE ANALYSIS  //  SESSION END",
                    26,UnityEngine.TextAnchor.MiddleCenter,vfColor(vfMuted));
                var edge = vfPlate("Title accent",vfRoot.transform,
                    new UnityEngine.Vector2(0,154),new UnityEngine.Vector2(560,4),
                    vfColor(vfAccent));
                var lead = vfPlate("Content lead",vfRoot.transform,
                    new UnityEngine.Vector2(0,96),new UnityEngine.Vector2(1180,78),
                    vfColor(vfMid));
                vfLabel(lead,vfScreen == "Menu" ? "SYSTEM READY  •  AWAITING INPUT" :
                    vfScreen == "Quiz" ? "Which option best completes the mission?" :
                    "Outstanding performance. Your results are ready.",
                    31,UnityEngine.TextAnchor.MiddleCenter,vfColor(vfText));
                var vfChoices = vfScreen == "Menu"
                    ? new[] {"START MISSION","TRAINING MODULES","OPERATIONS LOG","SETTINGS"}
                    : vfScreen == "Quiz"
                    ? new[] {"01   Primary answer","02   Secondary answer",
                        "03   Alternate answer","04   Final answer"}
                    : new[] {"VIEW REPORT","RETRY CHALLENGE","NEXT MISSION","MAIN MENU"};
                for (int i=0; i<vfChoices.Length; i++) {
                    var y = 6 - i * 112;
                    var buttonPlate = vfPlate("Action " + (i+1),vfRoot.transform,
                        new UnityEngine.Vector2(0,y),
                        new UnityEngine.Vector2(1110,92),vfColor(vfPanel));
                    var outline = buttonPlate.gameObject.AddComponent<
                        UnityEngine.UI.Outline>();
                    outline.effectColor = i==0 ? vfColor(vfAccent) : vfColor(vfSecondary)*.5f;
                    outline.effectDistance = new UnityEngine.Vector2(2,-2);
                    var button = buttonPlate.gameObject.AddComponent<UnityEngine.UI.Button>();
                    button.targetGraphic = buttonPlate.GetComponent<UnityEngine.UI.Image>();
                    button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
                    var colors = button.colors;
                    colors.normalColor = UnityEngine.Color.white;
                    colors.highlightedColor = vfColor(vfAccent);
                    colors.pressedColor = vfColor(vfSecondary);
                    colors.selectedColor = vfColor(vfAccent);
                    colors.fadeDuration = .16f;
                    button.colors = colors;
                    vfLabel(buttonPlate,vfChoices[i],32,UnityEngine.TextAnchor.MiddleLeft,
                        vfColor(vfText));
                    var bar = vfPlate("Choice indicator " + (i+1),buttonPlate,
                        new UnityEngine.Vector2(-533,0),new UnityEngine.Vector2(5,62),
                        i==0 ? vfColor(vfAccent) : vfColor(vfSecondary));
                }
                var footer = vfPlate("Footer",vfRoot.transform,
                    new UnityEngine.Vector2(0,-483),
                    new UnityEngine.Vector2(1660,48),
                    new UnityEngine.Color(0,0,0,0));
                vfLabel(footer,"VISUAL FOUNDRY  //  PREMIUM INTERFACE  //  " +
                    vfStyle.ToUpperInvariant(),18,
                    UnityEngine.TextAnchor.MiddleCenter,vfColor(vfMuted));
                var anim = vfRoot.AddComponent<UnityEngine.Animation>();
                var clipPath = vfOutput + "/" + vfScreen + "-intro.anim";
                var clip = new UnityEngine.AnimationClip();
                clip.legacy = true;
                clip.frameRate = 60;
                clip.SetCurve("",typeof(UnityEngine.CanvasGroup),"m_Alpha",
                    UnityEngine.AnimationCurve.EaseInOut(0f,0f,.65f,1f));
                UnityEditor.AssetDatabase.CreateAsset(clip,clipPath);
                anim.AddClip(clip,"Intro");
                anim.clip = clip;
                anim.playAutomatically = true;
                var prefabPath = vfOutput + "/" + vfScreen + ".prefab";
                UnityEditor.PrefabUtility.SaveAsPrefabAsset(vfRoot,prefabPath);
                UnityEngine.Object.DestroyImmediate(vfRoot);
                vfPrefabPaths.Add(prefabPath);
            }
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Additive);
            try {
                var rootPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<
                    UnityEngine.GameObject>(vfPrefabPaths[1]);
                if (rootPrefab == null)
                    throw new System.InvalidOperationException("VISUAL_FOUNDRY_PREFAB_MISSING");
                var instance = UnityEditor.PrefabUtility.InstantiatePrefab(rootPrefab,
                    preview) as UnityEngine.GameObject;
                if (instance == null)
                    throw new System.InvalidOperationException("VISUAL_FOUNDRY_INSTANCE_FAILED");
                var camera = new UnityEngine.GameObject("Preview Camera",
                    typeof(UnityEngine.Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera,preview);
                camera.GetComponent<UnityEngine.Camera>().clearFlags =
                    UnityEngine.CameraClearFlags.SolidColor;
                camera.GetComponent<UnityEngine.Camera>().backgroundColor =
                    vfColor(vfBack);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(preview,
                    vfOutput + "/Preview.unity");
            } finally {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(preview,true);
            }
            UnityEditor.AssetDatabase.SaveAssets();
            return "PROJECTHUB_VISUAL_FOUNDRY_READY|" + vfStyle + "|" +
                vfPrefabPaths.Count + "|" + vfOutput;
            """;
        return script
            .Replace("__OUTPUT__", JsonSerializer.Serialize(relativeAssets), StringComparison.Ordinal)
            .Replace("__STYLE__", JsonSerializer.Serialize(style.Id), StringComparison.Ordinal)
            .Replace("__BACK__", JsonSerializer.Serialize(style.Back), StringComparison.Ordinal)
            .Replace("__MID__", JsonSerializer.Serialize(style.Mid), StringComparison.Ordinal)
            .Replace("__PANEL__", JsonSerializer.Serialize(style.Panel), StringComparison.Ordinal)
            .Replace("__ACCENT__", JsonSerializer.Serialize(style.Accent), StringComparison.Ordinal)
            .Replace("__SECONDARY__", JsonSerializer.Serialize(style.Secondary), StringComparison.Ordinal)
            .Replace("__TEXT__", JsonSerializer.Serialize(style.Text), StringComparison.Ordinal)
            .Replace("__MUTED__", JsonSerializer.Serialize(style.Muted), StringComparison.Ordinal)
            .Replace("__SIGNATURE__", JsonSerializer.Serialize(style.Signature), StringComparison.Ordinal);
    }

    internal static async Task<Result> GenerateAsync(Plan plan,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<string>, CancellationToken, Task<UnityAssetCliResult>>? execute = null)
    {
        execute ??= (args, token) => UnityCliProcessRunner.RunAsync(
            plan.ProjectRoot,
            args, TimeSpan.FromSeconds(90), "UnityWorker Visual Foundry",
            "VISUAL_FOUNDRY_EXEC_TIMEOUT_UNVERIFIED", token);
        try
        {
            var args = UnityAssetCliImporter.ImportFileArguments(
                plan.ProjectRoot,
                plan.ScriptPath);
            var response = await execute(args, cancellationToken).ConfigureAwait(false);
            if (response.ExitCode != 0)
                return new(false, "VISUAL_FOUNDRY_CLI_FAILED: " +
                    Clip(response.Error + " " + response.Output), null);
            using var json = JsonDocument.Parse(response.Output);
            if (!json.RootElement.TryGetProperty("success", out var success) ||
                success.ValueKind != JsonValueKind.True ||
                !json.RootElement.TryGetProperty("data", out var data) ||
                !TryMarker(data, out var marker))
                return new(false, "VISUAL_FOUNDRY_ACK_UNVERIFIED", null);
            var expected = "PROJECTHUB_VISUAL_FOUNDRY_READY|" + plan.Theme.Id +
                "|3|" + plan.AssetDirectory;
            if (!string.Equals(marker, expected, StringComparison.Ordinal))
                return new(false, "VISUAL_FOUNDRY_ACK_MISMATCH: " + Clip(marker), null);
            // Editor confirms SaveAsPrefabAsset + AssetDatabase.SaveAssets,
            // but the Worker never mistakes this for visual/functional approval.
            return new(true, "VISUAL_FOUNDRY_GENERATED_UNINSPECTED: " +
                plan.AssetDirectory + "; Menu, Quiz, Results, Preview", plan.AssetDirectory);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Text.Json.JsonException or
            System.ComponentModel.Win32Exception)
        {
            return new(false, "VISUAL_FOUNDRY_UNVERIFIED: " + Clip(e.Message), null);
        }
    }

    private static bool TryMarker(JsonElement data, out string marker)
    {
        marker = string.Empty;
        if (data.ValueKind == JsonValueKind.String)
        {
            marker = data.GetString() ?? string.Empty;
            return marker.StartsWith("PROJECTHUB_VISUAL_FOUNDRY_READY|",
                StringComparison.Ordinal);
        }
        if (data.ValueKind != JsonValueKind.Object) return false;
        foreach (var name in new[] { "result", "value", "output" })
            if (data.TryGetProperty(name, out var item) && TryMarker(item, out marker))
                return true;
        return false;
    }

    private static string Clip(string message) =>
        message.Length > 700 ? message[..700] : message;
}
