namespace ProjectHub.Worker;

/// <summary>
/// Art-direction constraint solver for deterministic premium compositions.
/// It never uses AI to rank theme/layout candidates and never claims that
/// these numerical constraints substitute for rendered-image inspection.
/// </summary>
internal static class UnityVisualFoundryQuality
{
    internal sealed record Direction(
        string Layout, double TextContrast, double MutedContrast,
        double AccentContrast, double QualityBudget,
        IReadOnlyList<string> Notes);

    internal static Direction Evaluate(UnityVisualFoundry.Style style, string brief)
    {
        var layout = ChooseLayout(brief);
        var title = Contrast(style.Text, style.Panel);
        var muted = Contrast(style.Muted, style.Panel);
        var accent = Contrast(style.Accent, style.Panel);
        var notes = new List<string>();
        if (title < 4.5) notes.Add("TEXT_CONTRAST_BELOW_AA");
        if (muted < 4.5) notes.Add("SECONDARY_TEXT_CONTRAST_BELOW_AA");
        if (accent < 3) notes.Add("ACCENT_CONTRAST_LOW");
        // Coverage is a conservative floor for functional UI, not a
        // beauty metric or user-study-derived quality score.
        var budget = Math.Min(1, title / 7) * .5 +
                     Math.Min(1, muted / 4.5) * .3 +
                     Math.Min(1, accent / 3) * .2;
        return new(layout, Math.Round(title, 2), Math.Round(muted, 2),
            Math.Round(accent, 2), Math.Round(budget, 3), notes);
    }

    /// <summary>
    /// Mechanical accessibility self-correction applied BEFORE rendering.
    /// Changes only insufficiently legible palette entries, not game logic.
    /// </summary>
    internal static UnityVisualFoundry.Style AutoTune(UnityVisualFoundry.Style input)
    {
        return input with
        {
            Text = ReachContrast(input.Text, input.Panel, 4.5),
            Muted = ReachContrast(input.Muted, input.Panel, 4.5),
            Accent = ReachContrast(input.Accent, input.Panel, 3)
        };
    }

    private static string ReachContrast(string foreground, string background,
        double minimum)
    {
        if (Contrast(foreground,background) >= minimum) return foreground;
        var towardWhite = Contrast("#FFFFFF",background) >
            Contrast("#000000",background);
        var end = towardWhite ? "#FFFFFF" : "#000000";
        var r = Convert.ToInt32(foreground.Substring(1,2),16);
        var g = Convert.ToInt32(foreground.Substring(3,2),16);
        var b = Convert.ToInt32(foreground.Substring(5,2),16);
        var endChannel = towardWhite ? 255 : 0;
        for (var step = 1; step <= 20; step++)
        {
            var t = step/20.0;
            var color = "#" +
                ((int)Math.Round(r+(endChannel-r)*t)).ToString("X2") +
                ((int)Math.Round(g+(endChannel-g)*t)).ToString("X2") +
                ((int)Math.Round(b+(endChannel-b)*t)).ToString("X2");
            if (Contrast(color,background) >= minimum) return color;
        }
        return end;
    }

    internal static string ChooseLayout(string brief)
    {
        var text = brief ?? string.Empty;
        if (Has(text, "정보 밀도", "데이터", "대시보드", "편집", "editorial", "dense",
                "dashboard", "information", "카드형"))
            return "Editorial";
        if (Has(text, "몰입", "극장", "시네마", "영상", "대형", "immersive",
                "cinematic", "hero", "wide"))
            return "Immersive";
        return "Base";
    }

    internal static string PrefabPath(UnityVisualFoundry.Plan plan, string screen,
        string? requestedLayout = null)
    {
        if (screen is not ("Menu" or "Quiz" or "Results"))
            throw new ArgumentException("Unknown Visual Foundry screen.", nameof(screen));
        var layout = requestedLayout ?? "Base";
        if (layout is not ("Base" or "Editorial" or "Immersive"))
            throw new ArgumentException("Unknown Visual Foundry layout.", nameof(requestedLayout));
        return plan.AssetDirectory + "/" + screen +
            (layout == "Base" ? "" : "-" + layout) + ".prefab";
    }

    private static bool Has(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));

    internal static double Contrast(string first, string second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a,b) + .05) / (Math.Min(a,b) + .05);
    }

    private static double Luminance(string hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#')
            throw new ArgumentException("Expected #RRGGBB.", nameof(hex));
        var bytes = new double[3];
        for (var i = 0; i < 3; i++)
        {
            var v = Convert.ToInt32(hex.Substring(1+i*2,2),16) / 255.0;
            bytes[i] = v <= .04045 ? v/12.92 : Math.Pow((v+.055)/1.055,2.4);
        }
        return bytes[0]*.2126 + bytes[1]*.7152 + bytes[2]*.0722;
    }
}
