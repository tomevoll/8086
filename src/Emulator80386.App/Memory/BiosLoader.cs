using System;
using System.IO;
using Emulator80386.App.Config;

namespace Emulator80386.App.Memory
{
    public class BiosLoader
    {
        public static string ResolveRomPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (File.Exists(path)) return path;

            string? dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, path);
                if (File.Exists(candidate)) return candidate;

                string nameOnly = Path.GetFileName(path);
                string candidateInRoms = Path.Combine(dir, "roms", nameOnly);
                if (File.Exists(candidateInRoms)) return candidateInRoms;

                dir = Directory.GetParent(dir)?.FullName;
            }

            return path;
        }

        public static byte[] LoadOrGenerateBios(EmulatorConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            // 1. Check for split low/high ROM files
            string lowPath = ResolveRomPath(config.RomLowPath);
            string highPath = ResolveRomPath(config.RomHighPath);

            if (!string.IsNullOrEmpty(lowPath) && !string.IsNullOrEmpty(highPath) &&
                File.Exists(lowPath) && File.Exists(highPath))
            {
                byte[] low = File.ReadAllBytes(lowPath);
                byte[] high = File.ReadAllBytes(highPath);

                int mergedLength = (low.Length + high.Length);
                byte[] combined = new byte[mergedLength];

                int minLen = Math.Min(low.Length, high.Length);
                for (int i = 0; i < minLen; i++)
                {
                    combined[i * 2] = low[i];
                    combined[i * 2 + 1] = high[i];
                }
                return combined;
            }

            // 2. Check for unified ROM file
            string resolvedRom = ResolveRomPath(config.RomPath);
            if (File.Exists(resolvedRom))
            {
                return File.ReadAllBytes(resolvedRom);
            }

            throw new FileNotFoundException($"Required BIOS ROM file not found at '{config.RomPath}'. A valid motherboard BIOS file is required to boot.");
        }

        public static byte[] LoadOrGenerateVgaOptionRom(string vgaRomPath)
        {
            string resolvedPath = ResolveRomPath(string.IsNullOrEmpty(vgaRomPath) ? "roms/vgabios.bin" : vgaRomPath);
            if (File.Exists(resolvedPath))
            {
                return File.ReadAllBytes(resolvedPath);
            }

            // Generate standard VGA Option ROM at 0xC0000 (32KB / 64 blocks of 512 bytes)
            byte[] vgaRom = new byte[32 * 1024];
            vgaRom[0] = 0x55; // Magic byte 1
            vgaRom[1] = 0xAA; // Magic byte 2
            vgaRom[2] = 0x40; // Length = 64 * 512 bytes = 32KB
            vgaRom[3] = 0xCB; // RETF

            FixOptionRomChecksum(vgaRom);
            return vgaRom;
        }

        public static byte[] LoadOrGenerateIdeOptionRom(string ideRomPath)
        {
            string resolvedPath = ResolveRomPath(ideRomPath);
            if (File.Exists(resolvedPath))
            {
                return File.ReadAllBytes(resolvedPath);
            }

            // Generate standard IDE Option ROM at 0xC8000 (16KB / 32 blocks of 512 bytes)
            byte[] ideRom = new byte[16 * 1024];
            ideRom[0] = 0x55; // Magic byte 1
            ideRom[1] = 0xAA; // Magic byte 2
            ideRom[2] = 0x20; // Length = 32 * 512 bytes = 16KB
            ideRom[3] = 0xCB; // RETF

            FixOptionRomChecksum(ideRom);
            return ideRom;
        }

        public static void FixOptionRomChecksum(byte[] rom)
        {
            if (rom == null || rom.Length < 4) return;
            int len = rom[2] * 512;
            if (len <= 0 || len > rom.Length) len = rom.Length;

            byte sum = 0;
            for (int i = 0; i < len - 1; i++)
            {
                sum += rom[i];
            }
            rom[len - 1] = (byte)((0x100 - sum) & 0xFF);
        }
    }
}
