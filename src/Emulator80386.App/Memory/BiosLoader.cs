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

        public static byte[] GenerateOpenBios(int ramSizeMB)
        {
            byte[] bios = new byte[65536];

            // Setup 32-bit reset vector at offset 0xFFF0 (0xFFFFFFF0)
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
            bios[ptr++] = 0xBF; bios[ptr++] = 0x00; bios[ptr++] = 0x00; // MOV DI, 0x0000

            string msg = $"PC 80386 BIOS - {ramSizeMB}MB RAM - Drive C Ready";
            for (int i = 0; i < msg.Length; i++)
            {
                // ES: MOV byte ptr [DI], char (26 C6 05 <char>)
                bios[ptr++] = 0x26;
                bios[ptr++] = 0xC6; bios[ptr++] = 0x05;
                bios[ptr++] = (byte)msg[i];
                bios[ptr++] = 0x47; // INC DI

                // ES: MOV byte ptr [DI], 0x1F (white on blue) (26 C6 05 1F)
                bios[ptr++] = 0x26;
                bios[ptr++] = 0xC6; bios[ptr++] = 0x05;
                bios[ptr++] = 0x1F;
                bios[ptr++] = 0x47; // INC DI
            }

            // HLT
            bios[ptr++] = 0xF4;

            return bios;
        }
    }
}
