using System;
using System.IO;
using System.Text.Json;

namespace Amiga500.Config;

public enum VideoStandard
{
    PAL,
    NTSC
}

public class EmulatorConfig
{
    public string KickstartRomPath { get; set; } = "roms/kick13.rom";
    public int ChipRamKB { get; set; } = 512; // 512 KB standard
    public int SlowRamKB { get; set; } = 0;   // e.g. 512 KB trapdoor expansion ($C00000)
    public int FastRamKB { get; set; } = 0;   // e.g. 2MB / 8MB fast RAM
    public VideoStandard VideoStandard { get; set; } = VideoStandard.PAL;
    public string?[] FloppyPaths { get; set; } = new string?[4]; // DF0, DF1, DF2, DF3
    public float AudioVolume { get; set; } = 1.0f;
    public bool EnableAudio { get; set; } = true;
    public int DisplayScale { get; set; } = 2;

    private const string ConfigFileName = "config.json";

    public static EmulatorConfig Load()
    {
        if (File.Exists(ConfigFileName))
        {
            try
            {
                string json = File.ReadAllText(ConfigFileName);
                var config = JsonSerializer.Deserialize<EmulatorConfig>(json);
                if (config != null) return config;
            }
            catch
            {
                // Fall back to default on error
            }
        }
        return new EmulatorConfig();
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFileName, json);
        }
        catch
        {
            // Ignore save errors if read-only filesystem
        }
    }
}
