using System;
using System.Collections.Generic;
using System.IO;
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

/// <summary>User settings, stored as JSON in %APPDATA%\ClusterWidget\config.json. Colors are #AARRGGBB or #RRGGBB.</summary>
internal sealed class Config
{
    /// <summary>True: the box snaps to the desktop icon grid. False: it moves freely, pixel by pixel.</summary>
    public bool Snap { get; set; } = true;
    /// <summary>Top-left grid cell when snapping (desktop-icon cells, from the work-area corner).</summary>
    public int X { get; set; } = 1;
    public int Y { get; set; } = 1;
    /// <summary>Top-left screen pixel when moving freely.</summary>
    public int FreeX { get; set; } = 100;
    public int FreeY { get; set; } = 100;
    /// <summary>How many icons the closed box shows (the last one becomes a preview of the rest when there are more).</summary>
    public int ClosedCount { get; set; } = 4;
    /// <summary>Background picture (png, jpg, bmp, gif, tif) drawn inside the box instead of the blurred wallpaper. Empty = none.</summary>
    public string BackgroundImage { get; set; } = "";
    /// <summary>Picture zoom in percent (100 = just covers the box).</summary>
    public int ImageZoom { get; set; } = 100;
    /// <summary>Which part of a zoomed picture shows, 0-100 (50 = centred).</summary>
    public int ImagePanX { get; set; } = 50;
    public int ImagePanY { get; set; } = 50;
    /// <summary>Distance of the settings icon from the box edge, horizontally / vertically, in pixels at 100%. -1 = automatic.</summary>
    public int GearOffsetX { get; set; } = -1;
    public int GearOffsetY { get; set; } = -1;
    /// <summary>
    /// Icons per row and per column of the closed box. 0 in either = automatic. The box is always exactly
    /// the icon grid plus the padding, so it grows or shrinks with the padding; icons never shrink.
    /// </summary>
    public int BoxCols { get; set; } = 0;
    public int BoxRows { get; set; } = 0;
    /// <summary>Keep the expanded cluster open when clicking elsewhere (handy while editing settings).</summary>
    public bool KeepOpen { get; set; } = false;
    /// <summary>Opacity of the outline, 0-100 (on top of the outline color's own alpha).</summary>
    public int OutlineOpacity { get; set; } = 100;
    /// <summary>Settings icon corner: auto, topRight, topLeft, bottomRight, bottomLeft. Auto picks a corner that overlaps nothing.</summary>
    public string GearCorner { get; set; } = "auto";
    /// <summary>Settings icon size in pixels at 100%; 0 = automatic (scales with the icons).</summary>
    public int GearSize { get; set; } = 0;
    /// <summary>Space between the box edge and the icons, closed. Pixels at 100%.</summary>
    public float ClosedPadding { get; set; } = 18f;
    /// <summary>Space between icons, closed. Pixels at 100%.</summary>
    public float ClosedGap { get; set; } = 12f;
    /// <summary>Space between the box edge and the icons, expanded. Pixels at 100%.</summary>
    public float OpenPadding { get; set; } = 14f;
    /// <summary>Space between icons, expanded. Pixels at 100%.</summary>
    public float OpenGap { get; set; } = 8f;
    /// <summary>Opacity of the whole square (blur + tint), 0-100. Lower it to see the desktop through the box.</summary>
    public int Opacity { get; set; } = 100;
    /// <summary>Color painted over the blur; its alpha is the tint strength.</summary>
    public string Tint { get; set; } = "#8C14181C";
    /// <summary>Blur radius in pixels (0 = sharp wallpaper).</summary>
    public int Blur { get; set; } = 24;
    public string OutlineColor { get; set; } = "#FF4CC2FF";
    /// <summary>Pixels at 100% scaling.</summary>
    public float OutlineWidth { get; set; } = 2f;
    /// <summary>Pixels at 100% scaling; any value works (half the shorter side makes a pill).</summary>
    public float CornerRadius { get; set; } = 40f;
    /// <summary>Space kept between the box edge and the cell border, so neighbours do not touch.</summary>
    public float Gap { get; set; } = 4f;
    /// <summary>Multiplier on the Windows desktop icon size.</summary>
    public float IconScale { get; set; } = 1f;
    public bool ShowLabels { get; set; } = true;
    public string LabelColor { get; set; } = "#FFF2F2F2";
    /// <summary>Label font size in pixels at 100% scaling.</summary>
    public float LabelSize { get; set; } = 12f;
    public string FontFamily { get; set; } = "Segoe UI";
    public string HoverColor { get; set; } = "#2EFFFFFF";
    public int AnimationMs { get; set; } = 160;
    public List<Item> Items { get; set; } = new();

    /// <summary>Config folder. CLUSTERWIDGET_DIR overrides it (used to test a second instance without touching real settings).</summary>
    public static string Dir => Environment.GetEnvironmentVariable("CLUSTERWIDGET_DIR") is { Length: > 0 } d
        ? d
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClusterWidget");
    public static string FilePath => System.IO.Path.Combine(Dir, "config.json");

    public static Config Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var cfg = JsonSerializer.Deserialize(File.ReadAllText(FilePath), ConfigJson.Default.Config);
                if (cfg != null) { cfg.Sanitize(); return cfg; }
            }
        }
        catch (Exception) { /* malformed file: fall through to defaults, keep the file untouched */ }
        var def = new Config();
        def.Save();
        return def;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, ConfigJson.Default.Config));
        }
        catch (Exception) { /* read-only profile etc.: settings simply do not persist */ }
    }

    /// <summary>Restores the look (not position or items) to the shipped defaults.</summary>
    public void ResetLook()
    {
        var d = new Config();
        ClosedCount = d.ClosedCount; BoxCols = d.BoxCols; BoxRows = d.BoxRows; OutlineOpacity = d.OutlineOpacity;
        GearCorner = d.GearCorner; GearSize = d.GearSize; GearOffsetX = d.GearOffsetX; GearOffsetY = d.GearOffsetY;
        BackgroundImage = d.BackgroundImage; ImageZoom = d.ImageZoom; ImagePanX = d.ImagePanX; ImagePanY = d.ImagePanY; ClosedPadding = d.ClosedPadding; ClosedGap = d.ClosedGap;
        OpenPadding = d.OpenPadding; OpenGap = d.OpenGap;
        Opacity = d.Opacity; Tint = d.Tint; Blur = d.Blur; OutlineColor = d.OutlineColor; OutlineWidth = d.OutlineWidth;
        CornerRadius = d.CornerRadius; IconScale = d.IconScale; ShowLabels = d.ShowLabels;
    }

    private void Sanitize()
    {
        ClosedCount = Math.Clamp(ClosedCount, 1, 16);
        BackgroundImage ??= "";
        ImageZoom = Math.Clamp(ImageZoom, 100, 400);
        ImagePanX = Math.Clamp(ImagePanX, 0, 100);
        ImagePanY = Math.Clamp(ImagePanY, 0, 100);
        GearOffsetX = Math.Clamp(GearOffsetX, -1, 120);
        GearOffsetY = Math.Clamp(GearOffsetY, -1, 120);
        BoxCols = Math.Clamp(BoxCols, 0, 20);
        BoxRows = Math.Clamp(BoxRows, 0, 20);
        OutlineOpacity = Math.Clamp(OutlineOpacity, 0, 100);
        GearSize = Math.Clamp(GearSize, 0, 60);
        GearCorner ??= "auto";
        ClosedPadding = Math.Clamp(ClosedPadding, 0f, 80f);
        ClosedGap = Math.Clamp(ClosedGap, 0f, 80f);
        OpenPadding = Math.Clamp(OpenPadding, 0f, 80f);
        OpenGap = Math.Clamp(OpenGap, 0f, 80f);
        Blur = Math.Clamp(Blur, 0, 100);
        Opacity = Math.Clamp(Opacity, 0, 100);
        OutlineWidth = Math.Clamp(OutlineWidth, 0f, 20f);
        CornerRadius = Math.Clamp(CornerRadius, 0f, 200f);
        IconScale = Math.Clamp(IconScale, 0.3f, 3f);
        LabelSize = Math.Clamp(LabelSize, 6f, 32f);
        AnimationMs = Math.Clamp(AnimationMs, 0, 2000);
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

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Config))]
internal partial class ConfigJson : JsonSerializerContext { }
