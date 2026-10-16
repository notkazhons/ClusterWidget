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
    /// <summary>Top-left grid cell of the widget (desktop-icon cells, from the work-area corner).</summary>
    public int X { get; set; } = 1;
    public int Y { get; set; } = 1;
    /// <summary>Cells per tile side. The collapsed widget is 2 tiles wide, so 2 gives the 4x4-cell footprint.</summary>
    public int TileCells { get; set; } = 2;
    /// <summary>"acrylic" (true acrylic, system-fixed 8px corners) or "blur" (lighter blur, honors cornerRadius).</summary>
    public string Backdrop { get; set; } = "acrylic";
    /// <summary>Tint painted over the blur (alpha = how much color covers it).</summary>
    public string Tint { get; set; } = "#9E14181C";
    public string OutlineColor { get; set; } = "#FF4CC2FF";
    /// <summary>Pixels at 100% scaling.</summary>
    public float OutlineWidth { get; set; } = 2f;
    public float CornerRadius { get; set; } = 26f;
    /// <summary>Space kept between the widget edge and the cell border, so neighbours do not touch.</summary>
    public float Gap { get; set; } = 4f;
    /// <summary>Icon size as a fraction of the tile's shorter side.</summary>
    public float IconRatio { get; set; } = 0.46f;
    public bool ShowLabels { get; set; } = true;
    public string LabelColor { get; set; } = "#FFF2F2F2";
    /// <summary>Label font size as a fraction of the tile's shorter side.</summary>
    public float LabelSize { get; set; } = 0.11f;
    public string FontFamily { get; set; } = "Segoe UI";
    public string HoverColor { get; set; } = "#2EFFFFFF";
    public int AnimationMs { get; set; } = 180;
    public List<Item> Items { get; set; } = new();

    public static string Dir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClusterWidget");
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

    private void Sanitize()
    {
        TileCells = Math.Clamp(TileCells, 1, 4);
        IconRatio = Math.Clamp(IconRatio, 0.1f, 0.9f);
        LabelSize = Math.Clamp(LabelSize, 0.05f, 0.3f);
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
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Config))]
internal partial class ConfigJson : JsonSerializerContext { }
