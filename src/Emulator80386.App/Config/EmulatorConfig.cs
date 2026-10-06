using System;
using System.Collections.Generic;
using System.IO;

namespace Emulator80386.App.Config
{
    public class EmulatorConfig
    {
        public int RamSizeMB { get; set; } = 16;
        public string RomPath { get; set; } = "bios.bin";
        public string RomLowPath { get; set; } = "";
        public string RomHighPath { get; set; } = "";
        public string VgaRomPath { get; set; } = "vgarom.bin";
        public string IdeRomPath { get; set; } = "iderom.bin";
        public string DriveCFolder { get; set; } = "./hdd";
        public int DisplayWidth { get; set; } = 640;
        public int DisplayHeight { get; set; } = 400;

        public static EmulatorConfig LoadFromFile(string filePath)
        {
            var config = new EmulatorConfig();
            if (!File.Exists(filePath))
            {
                config.SaveToFile(filePath);
                return config;
            }

            string currentSection = "";
            var lines = File.ReadAllLines(filePath);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";") || line.StartsWith("#"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                    continue;
                }

                var parts = line.Split('=', 2);
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim().ToLowerInvariant();
                var value = parts[1].Trim();

                switch (key)
                {
                    case "ramsizemb":
                    case "ram_size_mb":
                        if (int.TryParse(value, out int ram)) config.RamSizeMB = Math.Max(1, ram);
                        break;
                    case "rompath":
                    case "rom_path":
                    case "biospath":
                        config.RomPath = value;
                        break;
                    case "romlowpath":
                    case "rom_low_path":
                    case "bioslowpath":
                        config.RomLowPath = value;
                        break;
                    case "romhighpath":
                    case "rom_high_path":
                    case "bioshighpath":
                        config.RomHighPath = value;
                        break;
                    case "vgarompath":
                    case "vga_rom_path":
                        config.VgaRomPath = value;
                        break;
                    case "iderompath":
                    case "ide_rom_path":
                        config.IdeRomPath = value;
                        break;
                    case "drivecfolder":
                    case "drive_c_folder":
                    case "hddpath":
                        config.DriveCFolder = value;
                        break;
                    case "displaywidth":
                    case "width":
                        if (int.TryParse(value, out int w)) config.DisplayWidth = Math.Max(320, w);
                        break;
                    case "displayheight":
                    case "height":
                        if (int.TryParse(value, out int h)) config.DisplayHeight = Math.Max(200, h);
                        break;
                }
            }

            return config;
        }

        public void SaveToFile(string filePath)
        {
            var lines = new List<string>
            {
                "[Memory]",
                $"RamSizeMB={RamSizeMB}",
                "",
                "[BIOS]",
                $"RomPath={RomPath}",
                $"RomLowPath={RomLowPath}",
                $"RomHighPath={RomHighPath}",
                $"VgaRomPath={VgaRomPath}",
                $"IdeRomPath={IdeRomPath}",
                "",
                "[Storage]",
                $"DriveCFolder={DriveCFolder}",
                "",
                "[Display]",
                $"DisplayWidth={DisplayWidth}",
                $"DisplayHeight={DisplayHeight}"
            };

            File.WriteAllLines(filePath, lines);
        }
    }
}
