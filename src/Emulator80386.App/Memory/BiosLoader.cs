using System;
using System.IO;
using Emulator80386.App.Config;

namespace Emulator80386.App.Memory
{
    public class BiosLoader
    {
        public static byte[] LoadOrGenerateBios(EmulatorConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            // 1. Check for split low/high ROM files (e.g. 32KB low + 32KB high = 64KB ROM)
            if (!string.IsNullOrEmpty(config.RomLowPath) && !string.IsNullOrEmpty(config.RomHighPath) &&
                File.Exists(config.RomLowPath) && File.Exists(config.RomHighPath))
            {
                byte[] low = File.ReadAllBytes(config.RomLowPath);
                byte[] high = File.ReadAllBytes(config.RomHighPath);

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
            if (!string.IsNullOrEmpty(config.RomPath) && File.Exists(config.RomPath))
            {
                return File.ReadAllBytes(config.RomPath);
            }

            // 3. Fallback: Generate open BIOS ROM image (64KB)
            return GenerateOpenBios(config.RamSizeMB);
        }

        public static byte[] LoadOrGenerateVgaOptionRom(string vgaRomPath)
        {
            if (!string.IsNullOrEmpty(vgaRomPath) && File.Exists(vgaRomPath))
            {
                return File.ReadAllBytes(vgaRomPath);
            }

            // Generate standard VGA Option ROM at 0xC0000 (32KB / 64 blocks of 512 bytes)
            byte[] vgaRom = new byte[32 * 1024];
            vgaRom[0] = 0x55; // Magic byte 1
            vgaRom[1] = 0xAA; // Magic byte 2
            vgaRom[2] = 0x40; // Length = 64 * 512 bytes = 32KB
            vgaRom[3] = 0xCB; // RETF (Return Far immediately upon BIOS POST call)

            return vgaRom;
        }

        public static byte[] LoadOrGenerateIdeOptionRom(string ideRomPath)
        {
            if (!string.IsNullOrEmpty(ideRomPath) && File.Exists(ideRomPath))
            {
                return File.ReadAllBytes(ideRomPath);
            }

            // Generate standard IDE Option ROM at 0xC8000 (16KB / 32 blocks of 512 bytes)
            byte[] ideRom = new byte[16 * 1024];
            ideRom[0] = 0x55; // Magic byte 1
            ideRom[1] = 0xAA; // Magic byte 2
            ideRom[2] = 0x20; // Length = 32 * 512 bytes = 16KB
            ideRom[3] = 0xCB; // RETF

            return ideRom;
        }

        public static byte[] GenerateOpenBios(int ramSizeMB)
        {
            byte[] bios = new byte[65536];

            // 32-bit reset vector at offset 0xFFF0 (0xFFFFFFF0)
            // Far jump to 0xF000:0x0000 (EA 00 00 00 F0)
            bios[0xFFF0] = 0xEA;
            bios[0xFFF1] = 0x00;
            bios[0xFFF2] = 0x00;
            bios[0xFFF3] = 0x00;
            bios[0xFFF4] = 0xF0;

            // BIOS Entry point at 0x0000 (0xF000:0x0000)
            int ptr = 0;
            bios[ptr++] = 0xFC; // CLD
            bios[ptr++] = 0xB8; bios[ptr++] = 0x00; bios[ptr++] = 0xB8; // MOV AX, 0xB800
            bios[ptr++] = 0x8E; bios[ptr++] = 0xC0; // MOV ES, AX

            // Clear Screen (2000 cells of 0x1F20: white on blue space)
            bios[ptr++] = 0xB8; bios[ptr++] = 0x20; bios[ptr++] = 0x1F; // MOV AX, 0x1F20
            bios[ptr++] = 0xBF; bios[ptr++] = 0x00; bios[ptr++] = 0x00; // MOV DI, 0x0000
            bios[ptr++] = 0xB9; bios[ptr++] = 0xD0; bios[ptr++] = 0x07; // MOV CX, 2000
            bios[ptr++] = 0xF3; bios[ptr++] = 0xAB; // REP STOSW

            // Line 1: Header
            string line1 = $"PC 80386 BIOS - {ramSizeMB}MB RAM - Drive C Ready";
            bios[ptr++] = 0xBF; bios[ptr++] = 0x00; bios[ptr++] = 0x00; // MOV DI, 0x0000
            for (int i = 0; i < line1.Length; i++)
            {
                bios[ptr++] = 0x26; bios[ptr++] = 0xC6; bios[ptr++] = 0x05; bios[ptr++] = (byte)line1[i]; bios[ptr++] = 0x47;
                bios[ptr++] = 0x26; bios[ptr++] = 0xC6; bios[ptr++] = 0x05; bios[ptr++] = 0x1F; bios[ptr++] = 0x47;
            }

            // Line 2: Status
            string line2 = "Status: Initialized and Booting from Drive C...";
            bios[ptr++] = 0xBF; bios[ptr++] = 0xA0; bios[ptr++] = 0x00; // MOV DI, 160 (row 1, col 0)
            for (int i = 0; i < line2.Length; i++)
            {
                bios[ptr++] = 0x26; bios[ptr++] = 0xC6; bios[ptr++] = 0x05; bios[ptr++] = (byte)line2[i]; bios[ptr++] = 0x47;
                bios[ptr++] = 0x26; bios[ptr++] = 0xC6; bios[ptr++] = 0x05; bios[ptr++] = 0x1E; bios[ptr++] = 0x47; // Yellow on Blue
            }

            // HLT
            bios[ptr++] = 0xF4;

            return bios;
        }
    }
}
