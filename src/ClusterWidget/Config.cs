using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClusterWidget;

/// <summary>One launchable entry: a file, folder, shortcut or <c>shell:</c> path.</summary>
internal sealed class Item
{
    public string Path { get; set; } = "";
    public string? Name { get; set; }

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name!;
            string p = Path.TrimEnd('\\', '/');
            string leaf = System.IO.Path.GetFileName(p);
            if (leaf.Length == 0) leaf = p;
            return leaf.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? leaf[..^4] : leaf;
        }
    }
}

/// <summary>
/// One cluster: its items, position and look. Stored as JSON in %APPDATA%\ClusterWidget\clusters\*.json, one file per
/// cluster. Colors are #AARRGGBB or #RRGGBB.
/// </summary>
internal sealed class Config
{
    /// <summary>Shown in the settings window and the tray menu.</summary>
    public string Name { get; set; } = "Cluster 1";

    // ---- position
    /// <summary>True: the box's top-left corner snaps to the desktop icon grid. False: it moves freely, pixel by pixel.</summary>
    public bool Snap { get; set; } = true;
    /// <summary>Top-left grid cell when snapping (desktop-icon cells, from the work-area corner).</summary>
    public int X { get; set; } = 1;
    public int Y { get; set; } = 1;
    /// <summary>Top-left screen pixel when moving freely.</summary>
    public int FreeX { get; set; } = 100;
    public int FreeY { get; set; } = 100;
    /// <summary>Obsolete (replaced by the expanded-view toggle in the settings window); kept so old files still load.</summary>
    public bool KeepOpen { get; set; } = false;

    // ---- layout (part of a saved design)
    /// <summary>How many icons the closed box shows (the last one becomes a preview of the rest when there are more).</summary>
    public int ClosedCount { get; set; } = 4;
    /// <summary>
    /// Icons per row and per column of the closed box. 0 in either = automatic. The box is always exactly
    /// the icon grid plus the padding, so it grows or shrinks with the padding; icons never shrink.
    /// </summary>
    public int BoxCols { get; set; } = 0;
    public int BoxRows { get; set; } = 0;
    /// <summary>Where the expanded view grows from: center (even growth all round) or topLeft (the top-left corner stays put and it grows right and down).</summary>
    public string ExpandFrom { get; set; } = "center";
    /// <summary>Multiplier on the Windows desktop icon size.</summary>
    public float IconScale { get; set; } = 1f;
    /// <summary>Space between the box edge and the icons, closed. Pixels at 100%.</summary>
    public float ClosedPadding { get; set; } = 18f;
    /// <summary>Space between icons, closed. Pixels at 100%.</summary>
    public float ClosedGap { get; set; } = 12f;
    /// <summary>Space between the box edge and the icons, expanded. Pixels at 100%.</summary>
    public float OpenPadding { get; set; } = 14f;
    /// <summary>Space between icons, expanded. Pixels at 100%.</summary>
    public float OpenGap { get; set; } = 8f;
    /// <summary>Space kept between the box edge and the cell border when snapping, so neighbours do not touch.</summary>
    public float Gap { get; set; } = 4f;

    // ---- look (part of a saved design)
    /// <summary>Opacity of the whole square (blur + tint), 0-100. Lower it to see the desktop through the box.</summary>
    public int Opacity { get; set; } = 100;
    /// <summary>Color painted over the blur; its alpha is the tint strength.</summary>
    public string Tint { get; set; } = "#8C14181C";
    /// <summary>Blur radius in pixels (0 = sharp wallpaper).</summary>
    public int Blur { get; set; } = 24;
    public string OutlineColor { get; set; } = "#FF4CC2FF";
    /// <summary>Opacity of the outline, 0-100 (on top of the outline color's own alpha).</summary>
    public int OutlineOpacity { get; set; } = 100;
    /// <summary>Pixels at 100% scaling.</summary>
    public float OutlineWidth { get; set; } = 2f;
    /// <summary>Pixels at 100% scaling; any value works (half the shorter side makes a pill).</summary>
    public float CornerRadius { get; set; } = 40f;
    /// <summary>Background picture (png, jpg, bmp, gif, tif) drawn over the tint. Empty = none.</summary>
    public string BackgroundImage { get; set; } = "";
    /// <summary>Opacity of the picture, 0-100.</summary>
    public int ImageOpacity { get; set; } = 100;
    /// <summary>Picture zoom in percent (100 = just covers the box).</summary>
    public int ImageZoom { get; set; } = 100;
    /// <summary>Which part of a zoomed picture shows, 0-100 (50 = centred).</summary>
    public int ImagePanX { get; set; } = 50;
    public int ImagePanY { get; set; } = 50;
    /// <summary>A second color painted over the picture (for example a slight white tint), and how strong it is (0-100).</summary>
    public string ImageOverlayColor { get; set; } = "#FFFFFFFF";
    public int ImageOverlayOpacity { get; set; } = 0;
    /// <summary>When there are more icons than fit closed: true = the last closed slot is a mini 2x2 preview of the rest, false = just another icon.</summary>
    public bool ClusterPreview { get; set; } = true;
    public bool ShowLabels { get; set; } = true;
    public string LabelColor { get; set; } = "#FFF2F2F2";
    /// <summary>Name size (expanded view) in pixels at 100% scaling.</summary>
    public float LabelSize { get; set; } = 12f;
    public string FontFamily { get; set; } = "Segoe UI";
    public string HoverColor { get; set; } = "#2EFFFFFF";
    public int AnimationMs { get; set; } = 160;

    // ---- settings icon (part of a saved design)
    /// <summary>Settings icon corner: auto, topRight, topLeft, bottomRight, bottomLeft. Auto picks a corner that overlaps nothing.</summary>
    public string GearCorner { get; set; } = "auto";
    /// <summary>Settings icon size in pixels at 100%; 0 = automatic (scales with the icons).</summary>
    public int GearSize { get; set; } = 0;
    /// <summary>Distance of the settings icon from the box edge, horizontally / vertically, in pixels at 100%. -1 = automatic.</summary>
    public int GearOffsetX { get; set; } = -1;
    public int GearOffsetY { get; set; } = -1;
    /// <summary>Show the settings icon all the time instead of only while the pointer is over the cluster.</summary>
    public bool GearAlways { get; set; } = false;

    public List<Item> Items { get; set; } = new();

    /// <summary>The file this cluster is stored in (not saved inside the file).</summary>
    [JsonIgnore] public string SourcePath { get; set; } = "";

    // ---------------------------------------------------------------- folders

    /// <summary>Settings folder. CLUSTERWIDGET_DIR overrides it (used to test a second instance without touching real settings).</summary>
    public static string RootDir => Environment.GetEnvironmentVariable("CLUSTERWIDGET_DIR") is { Length: > 0 } d
        ? d
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClusterWidget");
    public static string ClustersDir => System.IO.Path.Combine(RootDir, "clusters");
    public static string DesignsDir => System.IO.Path.Combine(RootDir, "designs");
    /// <summary>The single-cluster file of earlier versions; it becomes clusters\cluster-1.json on first start.</summary>
    public static string LegacyFile => System.IO.Path.Combine(RootDir, "config.json");

    // ---------------------------------------------------------------- load / save

    public static Config? Load(string path)
    {
        try
        {
            var cfg = JsonSerializer.Deserialize(File.ReadAllText(path), ConfigJson.Default.Config);
            if (cfg == null) return null;
            cfg.SourcePath = path;
            cfg.Sanitize();
            if (string.IsNullOrWhiteSpace(cfg.Name)) cfg.Name = System.IO.Path.GetFileNameWithoutExtension(path);
            return cfg;
        }
        catch (Exception) { return null; /* missing or malformed: the caller keeps what it has and the file stays untouched */ }
    }

    public void Save()
    {
        if (SourcePath.Length == 0) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SourcePath)!);
            File.WriteAllText(SourcePath, JsonSerializer.Serialize(this, ConfigJson.Default.Config));
        }
        catch (Exception) { /* read-only profile etc.: settings simply do not persist */ }
    }

    public Config Clone() => JsonSerializer.Deserialize(JsonSerializer.Serialize(this, ConfigJson.Default.Config), ConfigJson.Default.Config)!;

    /// <summary>Copies everything that is "design" (look, layout, settings icon) from <paramref name="d"/>; keeps items, name and position.</summary>
    public void ApplyDesign(Config d)
    {
        ExpandFrom = d.ExpandFrom; ClosedCount = d.ClosedCount; BoxCols = d.BoxCols; BoxRows = d.BoxRows; IconScale = d.IconScale;
        ClosedPadding = d.ClosedPadding; ClosedGap = d.ClosedGap; OpenPadding = d.OpenPadding; OpenGap = d.OpenGap; Gap = d.Gap;
        Opacity = d.Opacity; Tint = d.Tint; Blur = d.Blur; OutlineColor = d.OutlineColor; OutlineOpacity = d.OutlineOpacity;
        OutlineWidth = d.OutlineWidth; CornerRadius = d.CornerRadius;
        BackgroundImage = d.BackgroundImage; ImageOpacity = d.ImageOpacity; ImageOverlayColor = d.ImageOverlayColor; ImageOverlayOpacity = d.ImageOverlayOpacity; ClusterPreview = d.ClusterPreview; ImageZoom = d.ImageZoom; ImagePanX = d.ImagePanX; ImagePanY = d.ImagePanY;
        ShowLabels = d.ShowLabels; LabelColor = d.LabelColor; LabelSize = d.LabelSize; FontFamily = d.FontFamily; HoverColor = d.HoverColor;
        AnimationMs = d.AnimationMs;
        GearCorner = d.GearCorner; GearSize = d.GearSize; GearOffsetX = d.GearOffsetX; GearOffsetY = d.GearOffsetY; GearAlways = d.GearAlways;
        Sanitize();
    }

    /// <summary>Restores the design (not position, items or name) to the shipped defaults.</summary>
    public void ResetLook() => ApplyDesign(new Config());

    private void Sanitize()
    {
        Name ??= "Cluster";
        ExpandFrom = ExpandFrom?.Equals("topLeft", StringComparison.OrdinalIgnoreCase) == true ? "topLeft" : "center";
        ClosedCount = Math.Clamp(ClosedCount, 1, 16);
        BoxCols = Math.Clamp(BoxCols, 0, 20);
        BoxRows = Math.Clamp(BoxRows, 0, 20);
        IconScale = Math.Clamp(IconScale, 0.3f, 3f);
        ClosedPadding = Math.Clamp(ClosedPadding, 0f, 80f);
        ClosedGap = Math.Clamp(ClosedGap, 0f, 80f);
        OpenPadding = Math.Clamp(OpenPadding, 0f, 80f);
        OpenGap = Math.Clamp(OpenGap, 0f, 80f);
        Blur = Math.Clamp(Blur, 0, 100);
        Opacity = Math.Clamp(Opacity, 0, 100);
        OutlineOpacity = Math.Clamp(OutlineOpacity, 0, 100);
        OutlineWidth = Math.Clamp(OutlineWidth, 0f, 20f);
        CornerRadius = Math.Clamp(CornerRadius, 0f, 200f);
        BackgroundImage ??= "";
        ImageOpacity = Math.Clamp(ImageOpacity, 0, 100);
        ImageOverlayOpacity = Math.Clamp(ImageOverlayOpacity, 0, 100);
        ImageOverlayColor ??= "#FFFFFFFF";
        ImageZoom = Math.Clamp(ImageZoom, 100, 400);
        ImagePanX = Math.Clamp(ImagePanX, 0, 100);
        ImagePanY = Math.Clamp(ImagePanY, 0, 100);
        LabelSize = Math.Clamp(LabelSize, 6f, 32f);
        AnimationMs = Math.Clamp(AnimationMs, 0, 2000);
        GearSize = Math.Clamp(GearSize, 0, 60);
        GearOffsetX = Math.Clamp(GearOffsetX, -1, 120);
        GearOffsetY = Math.Clamp(GearOffsetY, -1, 120);
        GearCorner ??= "auto";
        Items ??= new();
    }

    /// <summary>Parses "#AARRGGBB" / "#RRGGBB" into a 32-bit ARGB value.</summary>
    public static uint Color(string s, uint fallback)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        string h = s.TrimStart('#');
        if (!uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out uint v)) return fallback;
        return h.Length switch { 6 => 0xFF000000u | v, 8 => v, _ => fallback };
    }

    public static string ColorText(uint argb) => "#" + argb.ToString("X8");
}

/// <summary>Named designs (look + layout + settings icon) that can be applied to any cluster.</summary>
internal static class Designs
{
    private static string FileFor(string name)
    {
        string safe = string.Concat(name.Trim().Select(c => Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c));
        return System.IO.Path.Combine(Config.DesignsDir, safe + ".json");
    }

    public static string[] List()
    {
        try
        {
            if (!Directory.Exists(Config.DesignsDir)) return Array.Empty<string>();
            return Directory.GetFiles(Config.DesignsDir, "*.json").Select(System.IO.Path.GetFileNameWithoutExtension).OfType<string>().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception) { return Array.Empty<string>(); }
    }

    public static bool Exists(string name) => File.Exists(FileFor(name));

    public static void Save(string name, Config source)
    {
        var copy = source.Clone();
        copy.Items = new();
        copy.Name = name;
        Directory.CreateDirectory(Config.DesignsDir);
        File.WriteAllText(FileFor(name), JsonSerializer.Serialize(copy, ConfigJson.Default.Config));
    }

    public static Config? Load(string name) => Config.Load(FileFor(name));

    public static void Delete(string name)
    {
        try { File.Delete(FileFor(name)); } catch (Exception) { /* already gone */ }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Config))]
internal partial class ConfigJson : JsonSerializerContext { }
