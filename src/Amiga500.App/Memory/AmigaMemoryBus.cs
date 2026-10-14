using System;
using Amiga500.Config;
using Amiga500.IO;

namespace Amiga500.Memory;

public class AmigaMemoryBus : IMemoryBus
{
    private const uint AddressMask = 0x00FFFFFF;

    private const uint ChipRamEnd = 0x00200000;
    private const uint FastRamStart = 0x00200000;
    private const uint FastRamEnd = 0x00A00000;

    private const uint CiaBStart = 0x00BFD000;
    private const uint CiaBEnd   = 0x00BFE000;

    private const uint CiaAStart = 0x00BFE000;
    private const uint CiaAEnd   = 0x00BFF000;

    private const uint SlowRamStart = 0x00C00000;
    private const uint SlowRamEnd   = 0x00C80000;

    private const uint CustomStart = 0x00DF0000;
    private const uint CustomEnd   = 0x00E00000;

    private const uint RomStart512 = 0x00F80000;
    private const uint RomEnd      = 0x01000000;

    private const int Rom256Size = 256 * 1024;
    private const int Rom512Size = 512 * 1024;

    public byte[] ChipRam { get; private set; }
    public byte[]? SlowRam { get; private set; }
    public byte[]? FastRam { get; private set; }

    /// <summary>Always 512 KB. A 256 KB image is stored twice so $F80000 mirrors $FC0000.</summary>
    public byte[] KickstartRom { get; private set; }

    public bool OverlayEnabled { get; set; } = true;
    public int KickstartSize { get; private set; }

    public ICiaDevice? CiaA { get; set; }
    public ICiaDevice? CiaB { get; set; }
    public ICustomRegisterDevice? CustomRegisters { get; set; }

    public bool LastAccessHasAddressError { get; set; }

    public AmigaMemoryBus(EmulatorConfig config, byte[] romData)
    {
        int chipSize = Math.Max(512 * 1024, config.ChipRamKB * 1024);
        ChipRam = new byte[chipSize];

        if (config.SlowRamKB > 0)
            SlowRam = new byte[config.SlowRamKB * 1024];

        if (config.FastRamKB > 0)
            FastRam = new byte[config.FastRamKB * 1024];

        KickstartRom = new byte[Rom512Size];
        LoadRom(romData);
    }

    public void LoadRom(byte[] romData)
    {
        Array.Clear(KickstartRom, 0, KickstartRom.Length);
        if (romData.Length == Rom256Size)
        {
            Array.Copy(romData, 0, KickstartRom, 0, Rom256Size);
            Array.Copy(romData, 0, KickstartRom, Rom256Size, Rom256Size);
            KickstartSize = Rom256Size;
        }
        else
        {
            Array.Copy(romData, KickstartRom, Math.Min(romData.Length, Rom512Size));
            KickstartSize = Rom512Size;
        }

        OverlayEnabled = true;
    }

    public void UpdateOverlayFromCiaAPortA(byte pra)
    {
        OverlayEnabled = (pra & 0x01) != 0;
    }

    private static bool IsCustom(uint address) => address >= CustomStart && address < CustomEnd;

    public byte ReadByte(uint rawAddress)
    {
        uint address = rawAddress & AddressMask;

        // OVL=1: ROM is visible at $000000-$07FFFF (maps second 256KB where reset vectors live).
        if (OverlayEnabled && address < 0x00080000)
        {
            int romOffset = (int)(0x40000 + (address & 0x3FFFF));
            return KickstartRom[romOffset];
        }

        if (address < ChipRamEnd)
            return ChipRam[(int)(address % (uint)ChipRam.Length)];

        if (address >= FastRamStart && address < FastRamEnd)
        {
            if (FastRam != null)
            {
                uint offset = address - FastRamStart;
                if (offset < FastRam.Length)
                    return FastRam[(int)offset];
            }
            return 0xFF;
        }

        if (address >= CiaBStart && address < CiaBEnd)
        {
            if ((address & 1) == 0 && CiaB != null)
                return CiaB.ReadRegister(address);
            return 0xFF;
        }

        if (address >= CiaAStart && address < CiaAEnd)
        {
            if ((address & 1) != 0 && CiaA != null)
                return CiaA.ReadRegister(address);
            return 0xFF;
        }

        if (address >= SlowRamStart && address < SlowRamEnd)
        {
            if (SlowRam != null)
            {
                uint offset = address - SlowRamStart;
                if (offset < SlowRam.Length)
                    return SlowRam[(int)offset];
            }
            return 0xFF;
        }

        if (IsCustom(address))
        {
            if (CustomRegisters == null)
                return 0xFF;

            uint offset = address & 0x1FE;
            ushort value = CustomRegisters.ReadRegister(offset);

            return (address & 1) == 0
                ? (byte)(value >> 8)
                : (byte)value;
        }

        // Kickstart: $F80000-$FFFFFF.
        if (address >= RomStart512 && address < RomEnd)
        {
            int offset = (int)((address - RomStart512) % (uint)KickstartRom.Length);
            return KickstartRom[offset];
        }

        return 0xFF;
    }

    public ushort ReadWord(uint rawAddress)
    {
        uint address = rawAddress & AddressMask;

        if ((address & 1) != 0)
        {
            LastAccessHasAddressError = true;
            return 0;
        }

        if (IsCustom(address))
            return CustomRegisters != null ? CustomRegisters.ReadRegister(address & 0x1FE) : (ushort)0xFFFF;

        return (ushort)(
            (ReadByte(address) << 8) |
             ReadByte(address + 1));
    }

    public uint ReadLong(uint rawAddress)
    {
        uint address = rawAddress & AddressMask;

        if ((address & 1) != 0)
        {
            LastAccessHasAddressError = true;
            return 0;
        }

        return ((uint)ReadWord(address) << 16) |
                ReadWord(address + 2);
    }

    public void WriteByte(uint rawAddress, byte value)
    {
        uint address = rawAddress & AddressMask;

        if (address < ChipRamEnd)
        {
            ChipRam[(int)(address % (uint)ChipRam.Length)] = value;
            return;
        }

        if (address >= FastRamStart && address < FastRamEnd)
        {
            if (FastRam != null)
            {
                uint offset = address - FastRamStart;
                if (offset < FastRam.Length)
                    FastRam[(int)offset] = value;
            }
            return;
        }

        if (address >= CiaBStart && address < CiaBEnd)
        {
            if ((address & 1) == 0)
                CiaB?.WriteRegister(address, value);
            return;
        }

        if (address >= CiaAStart && address < CiaAEnd)
        {
            if ((address & 1) != 0)
                CiaA?.WriteRegister(address, value);
            return;
        }

        if (address >= SlowRamStart && address < SlowRamEnd)
        {
            if (SlowRam != null)
            {
                uint offset = address - SlowRamStart;
                if (offset < SlowRam.Length)
                    SlowRam[(int)offset] = value;
            }
            return;
        }

        if (IsCustom(address))
        {
            if (CustomRegisters == null)
                return;

            uint offset = address & 0x1FE;
            CustomRegisters.WriteRegister(offset, (ushort)((value << 8) | value));
        }
    }

    public void WriteWord(uint rawAddress, ushort value)
    {
        uint address = rawAddress & AddressMask;

        if ((address & 1) != 0)
        {
            LastAccessHasAddressError = true;
            return;
        }

        if (IsCustom(address))
        {
            CustomRegisters?.WriteRegister(address & 0x1FE, value);
            return;
        }

        WriteByte(address, (byte)(value >> 8));
        WriteByte(address + 1, (byte)value);
    }

    public void WriteLong(uint rawAddress, uint value)
    {
        uint address = rawAddress & AddressMask;

        if ((address & 1) != 0)
        {
            LastAccessHasAddressError = true;
            return;
        }

        WriteWord(address, (ushort)(value >> 16));
        WriteWord(address + 2, (ushort)value);
    }
}
