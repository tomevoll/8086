using System;

namespace Emulator80386.App.Memory
{
    public class MemoryBus
    {
        public bool BiosShadowRead { get; set; }
        public bool BiosShadowWrite { get; set; }

        // Per-16KB segment PAM shadow configuration for 0xC0000 - 0xFFFFF (16 segments of 16KB)
        public bool[] ShadowRead { get; } = new bool[16];
        public bool[] ShadowWrite { get; } = new bool[16];

        public byte[] Ram { get; }
        public byte[] Vram { get; } = new byte[128 * 1024]; // 0xA0000 - 0xBFFFF (128 KB)
        public byte[] OptionRom { get; } = new byte[128 * 1024]; // 0xC0000 - 0xDFFFF (128 KB)
        public byte[] BiosRom { get; } = new byte[128 * 1024]; // 0xE0000 - 0xFFFFF (128 KB)

        public MemoryBus(int ramSizeMB)
        {
            int sizeInBytes = Math.Max(1, ramSizeMB) * 1024 * 1024;
            Ram = new byte[sizeInBytes];
            for (int i = 0; i < 16; i++)
            {
                ShadowWrite[i] = false;
                ShadowRead[i] = false;
            }
        }

        public void LoadBiosRom(byte[] romData)
        {
            if (romData == null || romData.Length == 0) return;
            if (romData.Length == 64 * 1024)
            {
                // Mirror 64KB ROM across both 0xE0000-0xEFFFF and 0xF0000-0xFFFFF
                Array.Copy(romData, 0, BiosRom, 0, 65536);
                Array.Copy(romData, 0, BiosRom, 65536, 65536);
            }
            else
            {
                int copyLength = Math.Min(romData.Length, BiosRom.Length);
                int offset = BiosRom.Length - copyLength;
                Array.Copy(romData, 0, BiosRom, offset, copyLength);
            }

            if (Ram.Length >= 0x100000)
            {
                Array.Copy(BiosRom, 0, Ram, 0xE0000, 128 * 1024);
            }
        }

        public void LoadVgaOptionRom(byte[] romData)
        {
            if (romData == null || romData.Length == 0) return;
            int copyLength = Math.Min(romData.Length, 64 * 1024); // Up to 64KB at 0xC0000
            Array.Copy(romData, 0, OptionRom, 0, copyLength);
        }

        public void LoadIdeOptionRom(byte[] romData)
        {
            if (romData == null || romData.Length == 0) return;
            int copyLength = Math.Min(romData.Length, 16 * 1024); // Up to 16KB at 0xD0000
            Array.Copy(romData, 0, OptionRom, 0x10000, copyLength);
        }

        public byte Read8(uint address)
        {
            // 32-bit Reset Vector / BIOS mirror (0xFFFE0000 - 0xFFFFFFFF)
            if (address >= 0xFFFE0000)
            {
                uint romOffset = address - 0xFFFE0000;
                return BiosRom[romOffset % BiosRom.Length];
            }

            // Video RAM (0xA0000 - 0xBFFFF)
            if (address >= 0xA0000 && address <= 0xBFFFF)
            {
                uint vramOffset = address - 0xA0000;
                return Vram[vramOffset % Vram.Length];
            }

            // Option ROMs / BIOS ROM / Shadow RAM Area (0xC0000 - 0xFFFFF)
            if (address >= 0xC0000 && address <= 0xFFFFF)
            {
                int seg = (int)((address - 0xC0000) / 0x4000);
                if (address <= 0xDFFFF)
                {
                    if ((ShadowRead[seg] || BiosShadowRead) && address < Ram.Length)
                    {
                        return Ram[address];
                    }
                    uint optOffset = address - 0xC0000;
                    return OptionRom[optOffset % OptionRom.Length];
                }
                else
                {
                    uint romOffset = address - 0xE0000;
                    if ((ShadowRead[seg] || BiosShadowRead || (address < Ram.Length && Ram[address] != BiosRom[romOffset])) && address < Ram.Length)
                    {
                        return Ram[address];
                    }
                    return BiosRom[romOffset];
                }
            }

            // RAM
            if (address < Ram.Length)
            {
                return Ram[address];
            }

            return 0xFF;
        }

        public void Write8(uint address, byte value)
        {
            // Video RAM (0xA0000 - 0xBFFFF)
            if (address >= 0xA0000 && address <= 0xBFFFF)
            {
                uint vramOffset = address - 0xA0000;
                Vram[vramOffset % Vram.Length] = value;
                return;
            }

            // Option ROM Area / BIOS ROM / Shadow RAM Area (0xC0000 - 0xFFFFF)
            if (address >= 0xC0000 && address <= 0xFFFFF)
            {
                int seg = (int)((address - 0xC0000) / 0x4000);
                if ((ShadowWrite[seg] || BiosShadowWrite) && address < Ram.Length)
                {
                    Ram[address] = value;
                }
                return;
            }

            // High reset vector mirror is read-only
            if (address >= 0xFFFE0000)
            {
                return;
            }

            // System RAM
            if (address < Ram.Length)
            {
                Ram[address] = value;
                return;
            }
        }

        public ushort Read16(uint address)
        {
            byte b0 = Read8(address);
            byte b1 = Read8(address + 1);
            return (ushort)(b0 | (b1 << 8));
        }

        public void Write16(uint address, ushort value)
        {
            Write8(address, (byte)(value & 0xFF));
            Write8(address + 1, (byte)((value >> 8) & 0xFF));
        }

        public uint Read32(uint address)
        {
            byte b0 = Read8(address);
            byte b1 = Read8(address + 1);
            byte b2 = Read8(address + 2);
            byte b3 = Read8(address + 3);
            return (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }

        public void Write32(uint address, uint value)
        {
            Write8(address, (byte)(value & 0xFF));
            Write8(address + 1, (byte)((value >> 8) & 0xFF));
            Write8(address + 2, (byte)((value >> 16) & 0xFF));
            Write8(address + 3, (byte)((value >> 24) & 0xFF));
        }
    }
}
