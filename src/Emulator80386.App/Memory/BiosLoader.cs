using System;
using System.IO;

namespace Emulator80386.App.Memory
{
    public class BiosLoader
    {
        public static byte[] LoadOrGenerateBios(string romPath, int ramSizeMB)
        {
            if (!string.IsNullOrEmpty(romPath) && File.Exists(romPath))
            {
                return File.ReadAllBytes(romPath);
            }

            // Generate an open BIOS image (64KB)
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
