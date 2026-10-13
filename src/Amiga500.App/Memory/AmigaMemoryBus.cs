using System;
using Amiga500.Config;
using Amiga500.IO;

namespace Amiga500.Memory;

public class AmigaMemoryBus : IMemoryBus
{
    public byte[] ChipRam { get; private set; }
    public byte[]? SlowRam { get; private set; }
    public byte[]? FastRam { get; private set; }
    public byte[] KickstartRom { get; private set; }
    public int KickstartRomSize { get; private set; }

    public bool OverlayEnabled { get; set; } = true;

    public ICiaDevice? CiaA { get; set; }
    public ICiaDevice? CiaB { get; set; }
    public ICustomRegisterDevice? CustomRegisters { get; set; }

    public AmigaMemoryBus(EmulatorConfig config, byte[] romData)
    {
        ChipRam = new byte[Math.Max(512 * 1024, config.ChipRamKB * 1024)];
        if (config.SlowRamKB > 0)
        {
            SlowRam = new byte[config.SlowRamKB * 1024];
        }
        if (config.FastRamKB > 0)
        {
            FastRam = new byte[config.FastRamKB * 1024];
        }

        KickstartRom = new byte[512 * 1024];
        LoadRom(romData);
    }

    public void LoadRom(byte[] romData)
    {
        Array.Clear(KickstartRom, 0, KickstartRom.Length);
        KickstartRomSize = romData.Length;

        if (romData.Length == 256 * 1024)
        {
            // Mirror 256KB twice into 512KB ROM area
            Array.Copy(romData, 0, KickstartRom, 0, 256 * 1024);
            Array.Copy(romData, 0, KickstartRom, 256 * 1024, 256 * 1024);
        }
        else if (romData.Length <= 512 * 1024)
        {
            Array.Copy(romData, KickstartRom, romData.Length);
        }
    }

    public byte ReadByte(uint rawAddress)
    {
        uint address = rawAddress & 0xFFFFFF;

        // $000000 - $1FFFFF: Chip RAM range
        if (address < 0x200000)
        {
            if (OverlayEnabled)
            {
                if (address < 0x080000)
                {
                    uint romOffset = (KickstartRomSize == 512 * 1024)
                        ? 0x40000 + (address & 0x3FFFF)
                        : (address & 0x3FFFF);
                    return KickstartRom[romOffset];
                }
                return 0xFF;
            }

            if (address < ChipRam.Length)
            {
                return ChipRam[address];
            }
            return 0xFF;
        }

        // $200000 - $9FFFFF: Fast RAM expansion
        if (address >= 0x200000 && address < 0xA00000)
        {
            uint fastOffset = address - 0x200000;
            if (FastRam != null && fastOffset < FastRam.Length)
            {
                return FastRam[fastOffset];
            }
            return 0xFF;
        }

        // CIA-A: $BFE000 - $BFEFFF or $FFE000 - $FFEFFF (odd byte lane $BFE001)
        if (((address & 0xFF0000) == 0xBF0000 || (address & 0xFF0000) == 0xFF0000) &&
            (address & 0x00F000) == 0x00E000 && CiaA != null)
        {
            return CiaA.ReadRegister(address);
        }

        // CIA-B: $BFD000 - $BFDFFF or $FFD000 - $FFDFFF (even byte lane $BFD000)
        if (((address & 0xFF0000) == 0xBF0000 || (address & 0xFF0000) == 0xFF0000) &&
            ((address & 0x00F000) == 0x00D000 || (address & 0x00F000) == 0x0000) && CiaB != null)
        {
            return CiaB.ReadRegister(address);
        }

        // $C00000 - $CFFFFF: Slow / Trapdoor RAM ($C00000 - $C7FFFF)
        if (address >= 0xC00000 && address < 0xD00000)
        {
            uint slowOffset = address - 0xC00000;
            if (SlowRam != null && slowOffset < SlowRam.Length)
            {
                return SlowRam[slowOffset];
            }
            return 0xFF;
        }

        // $DFF000 - $DFFFFF or $FFF000 - $FFFFFF: Custom Chip Registers
        if ((address >= 0xDFF000 && address < 0xE00000) || address >= 0xFFF000)
        {
            if (CustomRegisters != null)
            {
                uint regOffset = address & 0x1FE;
                ushort wordVal = CustomRegisters.ReadRegister(regOffset);
                return (address & 1) == 0 ? (byte)(wordVal >> 8) : (byte)(wordVal & 0xFF);
            }
            return 0xFF;
        }

        // $F80000 - $FFFFFF: Kickstart ROM
        if (address >= 0xF80000)
        {
            uint romOffset = address - 0xF80000;
            if (romOffset < KickstartRom.Length)
            {
                return KickstartRom[romOffset];
            }
            return 0xFF;
        }

        return 0xFF;
    }

    public ushort ReadWord(uint rawAddress)
    {
        uint address = rawAddress & 0xFFFFFF;

        if ((address >= 0xDFF000 && address < 0xE00000) || address >= 0xFFF000)
        {
            if (CustomRegisters != null)
            {
                return CustomRegisters.ReadRegister(address & 0x1FE);
            }
            return 0xFFFF;
        }

        return (ushort)((ReadByte(address) << 8) | ReadByte(address + 1));
    }

    public uint ReadLong(uint rawAddress)
    {
        uint address = rawAddress & 0xFFFFFF;
        return ((uint)ReadWord(address) << 16) | ReadWord(address + 2);
    }

    public void WriteByte(uint rawAddress, byte value)
    {
        uint address = rawAddress & 0xFFFFFF;

        // $000000 - $1FFFFF: Chip RAM
        if (address < 0x200000)
        {
            if (address < ChipRam.Length)
            {
                ChipRam[address] = value;
            }
            return;
        }

        // $200000 - $9FFFFF: Fast RAM expansion
        if (address >= 0x200000 && address < 0xA00000)
        {
            uint fastOffset = address - 0x200000;
            if (FastRam != null && fastOffset < FastRam.Length)
            {
                FastRam[fastOffset] = value;
            }
            return;
        }

        // CIA-A: $BFE000 - $BFEFFF or $FFE000 - $FFEFFF
        if (((address & 0xFF0000) == 0xBF0000 || (address & 0xFF0000) == 0xFF0000) &&
            (address & 0x00F000) == 0x00E000 && CiaA != null)
        {
            CiaA.WriteRegister(address, value);
            return;
        }

        // CIA-B: $BFD000 - $BFDFFF or $FFD000 - $FFDFFF
        if (((address & 0xFF0000) == 0xBF0000 || (address & 0xFF0000) == 0xFF0000) &&
            ((address & 0x00F000) == 0x00D000 || (address & 0x00F000) == 0x0000) && CiaB != null)
        {
            CiaB.WriteRegister(address, value);
            return;
        }

        // $C00000 - $CFFFFF: Slow / Trapdoor RAM
        if (address >= 0xC00000 && address < 0xD00000)
        {
            uint slowOffset = address - 0xC00000;
            if (SlowRam != null && slowOffset < SlowRam.Length)
            {
                SlowRam[slowOffset] = value;
            }
            return;
        }

        // $DFF000 - $DFFFFF or $FFF000 - $FFFFFF: Custom Chip Registers
        if ((address >= 0xDFF000 && address < 0xE00000) || address >= 0xFFF000)
        {
            if (CustomRegisters != null)
            {
                uint regOffset = address & 0x1FE;
                ushort wordValue = (address & 1) == 0 ? (ushort)(value << 8) : value;
                CustomRegisters.WriteRegister(regOffset, wordValue);
            }
            return;
        }
    }

    public void WriteWord(uint rawAddress, ushort value)
    {
        uint address = rawAddress & 0xFFFFFF;

        if ((address >= 0xDFF000 && address < 0xE00000) || address >= 0xFFF000)
        {
            if (CustomRegisters != null)
            {
                CustomRegisters.WriteRegister(address & 0x1FE, value);
            }
            return;
        }

        WriteByte(address, (byte)(value >> 8));
        WriteByte(address + 1, (byte)(value & 0xFF));
    }

    public void WriteLong(uint rawAddress, uint value)
    {
        uint address = rawAddress & 0xFFFFFF;
        WriteWord(address, (ushort)(value >> 16));
        WriteWord(address + 2, (ushort)(value & 0xFFFF));
    }
}
