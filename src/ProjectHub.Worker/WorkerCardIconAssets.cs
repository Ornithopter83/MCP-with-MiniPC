using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProjectHub.Worker;

/// <summary>
/// Portable icons for both XAML cards and dynamic history/pipeline cards.
/// The PNGs are embedded in the executable's managed assembly, so loading
/// does not depend on the install directory, the process current directory,
/// a fixed assembly-qualified pack URI or external Assets files.
/// </summary>
public static class WorkerCardIconAssets
{
    private const string Fallback = "current-console.png";
    private const string ResourcePrefix = "ProjectHub.Worker.Assets.";
    private static readonly HashSet<string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "current-console.png",
        "current-console-gray.png",
        "current-openai.png",
        "current-openai-gray.png",
        "current-muse.png",
        "current-web.png",
        "current-web-gray.png"
    };

    private static readonly ConcurrentDictionary<string, ImageSource> Sources =
        new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource Console => Load("current-console.png");
    public static ImageSource ConsoleGray => Load("current-console-gray.png");
    public static ImageSource OpenAi => Load("current-openai.png");
    public static ImageSource OpenAiGray => Load("current-openai-gray.png");
    public static ImageSource Muse => Load("current-muse.png");
    public static ImageSource Web => Load("current-web.png");
    public static ImageSource WebGray => Load("current-web-gray.png");

    public static ImageSource Load(string? asset)
    {
        // Provider and history data may contain an outdated/custom asset
        // name. Never interpret that name as an absolute or relative path.
        var name = asset is null ? null : Path.GetFileName(asset);
        if (name is null || !KnownNames.Contains(name) ||
            !string.Equals(name, asset, StringComparison.OrdinalIgnoreCase))
            name = Fallback;
        return Sources.GetOrAdd(name, CreateSource);
    }

    private static ImageSource CreateSource(string name)
    {
        try
        {
            using var stream = typeof(WorkerCardIconAssets).Assembly
                .GetManifestResourceStream(ResourcePrefix + name);
            if (stream is null)
                throw new FileNotFoundException(
                    "Embedded Worker card icon is missing: " + name);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception error) when (error is IOException or
                                      NotSupportedException or
                                      InvalidOperationException)
        {
            App.LogRuntimeFailure("WORKER_CARD_ICON_LOAD_FAILED asset=" + name,
                error);
            if (!string.Equals(name, Fallback, StringComparison.OrdinalIgnoreCase))
                return Load(Fallback);

            // A corrupt/missing fallback asset must not prevent app startup.
            var empty = new WriteableBitmap(1, 1, 96, 96,
                PixelFormats.Bgra32, null);
            empty.Freeze();
            return empty;
        }
    }
}
