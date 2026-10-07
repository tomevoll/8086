using System;

namespace Emulator80386.App.IO
{
    public class VgaController : IIOPortDevice
    {
        public byte MiscOutput { get; set; } = 0x63; // Color mode default (0x3Dx base)

        public byte ModeControl3D8 { get; set; } = 0x29; // Color mode control (Bit 3 = Video Enable)
        public byte ModeControl3B8 { get; set; } = 0x00; // Mono mode control (Bit 3 = Video Enable)

        public byte SequencerIndex { get; set; }
        public byte[] SequencerRegs { get; } = new byte[8];

        public byte CrtcIndex { get; set; }
        public byte[] CrtcRegs { get; } = new byte[64];

        public byte GraphicsIndex { get; set; }
        public byte[] GraphicsRegs { get; } = new byte[16];

        public byte AttributeIndex { get; set; }
        public byte[] AttributeRegs { get; } = new byte[32];
        public bool AttributeFlipFlop { get; set; } // false = expecting index, true = expecting data
        public bool PaletteAddressSource { get; set; } = true; // Bit 5 of 0x3C0

        public byte DacMask { get; set; } = 0xFF;
        public byte DacWriteIndex { get; set; }
        public byte DacReadIndex { get; set; }
        public byte DacState { get; set; } // 0 = write state, 3 = read state
        public byte DacRgbIndex { get; set; }
        public byte[] DacPalette { get; } = new byte[256 * 3];

        private byte _inputStatus1Toggle;

        public bool VideoEnabled => ((ModeControl3D8 & 0x08) != 0 || (ModeControl3B8 & 0x08) != 0) && PaletteAddressSource;

        public ushort CrtcStartWord => (ushort)((CrtcRegs[0x0C] << 8) | CrtcRegs[0x0D]);

        public byte Read8(ushort port)
        {
            switch (port)
            {
                case 0x3B8:
                    return ModeControl3B8;

                case 0x3D8:
                    return ModeControl3D8;

                case 0x3C0:
                    return AttributeIndex;

                case 0x3C1:
                    return AttributeRegs[AttributeIndex & 0x1F];

                case 0x3C2: // Input Status 0
                    return 0x60;

                case 0x3C4:
                    return SequencerIndex;

                case 0x3C5:
                    return SequencerRegs[SequencerIndex & 7];

                case 0x3C6:
                    return DacMask;

                case 0x3C7:
                    return DacState;

                case 0x3C8:
                    return DacWriteIndex;

                case 0x3C9:
                    {
                        byte val = DacPalette[(DacReadIndex * 3) + DacRgbIndex];
                        DacRgbIndex++;
                        if (DacRgbIndex >= 3)
                        {
                            DacRgbIndex = 0;
                            DacReadIndex++;
                        }
                        return val;
                    }

                case 0x3CC: // Read Misc Output
                    return MiscOutput;

                case 0x3CE:
                    return GraphicsIndex;

                case 0x3CF:
                    return GraphicsRegs[GraphicsIndex & 0x0F];

                case 0x3B4:
                case 0x3D4:
                    return CrtcIndex;

                case 0x3B5:
                case 0x3D5:
                    return CrtcRegs[CrtcIndex & 0x3F];

                case 0x3BA:
                case 0x3DA: // Input Status 1
                    {
                        AttributeFlipFlop = false; // Reset Attribute Controller flip-flop
                        _inputStatus1Toggle ^= 0x09; // Toggle bit 0 (display enable) and bit 3 (vertical retrace)
                        return (byte)(0x00 | _inputStatus1Toggle);
                    }

                default:
                    return 0xFF;
            }
        }

        public void Write8(ushort port, byte value)
        {
            switch (port)
            {
                case 0x3B8:
                    ModeControl3B8 = value;
                    break;

                case 0x3D8:
                    ModeControl3D8 = value;
                    break;

                case 0x3C0:
                    if (!AttributeFlipFlop)
                    {
                        AttributeIndex = (byte)(value & 0x1F);
                        PaletteAddressSource = (value & 0x20) != 0;
                    }
                    else
                    {
                        AttributeRegs[AttributeIndex & 0x1F] = value;
                    }
                    AttributeFlipFlop = !AttributeFlipFlop;
                    break;

                case 0x3C2: // Misc Output Write
                    MiscOutput = value;
                    break;

                case 0x3C4:
                    SequencerIndex = value;
                    break;

                case 0x3C5:
                    SequencerRegs[SequencerIndex & 7] = value;
                    break;

                case 0x3C6:
                    DacMask = value;
                    break;

                case 0x3C7: // DAC Read Index
                    DacReadIndex = value;
                    DacState = 3;
                    DacRgbIndex = 0;
                    break;

                case 0x3C8: // DAC Write Index
                    DacWriteIndex = value;
                    DacState = 0;
                    DacRgbIndex = 0;
                    break;

                case 0x3C9: // DAC Data Write
                    DacPalette[(DacWriteIndex * 3) + DacRgbIndex] = value;
                    DacRgbIndex++;
                    if (DacRgbIndex >= 3)
                    {
                        DacRgbIndex = 0;
                        DacWriteIndex++;
                    }
                    break;

                case 0x3CE:
                    GraphicsIndex = value;
                    break;

                case 0x3CF:
                    GraphicsRegs[GraphicsIndex & 0x0F] = value;
                    break;

                case 0x3B4:
                case 0x3D4:
                    CrtcIndex = value;
                    break;

                case 0x3B5:
                case 0x3D5:
                    {
                        byte index = (byte)(CrtcIndex & 0x3F);
                        bool protect = (CrtcRegs[0x11] & 0x80) != 0;
                        if (!protect || index > 7 || index == 7)
                        {
                            CrtcRegs[index] = value;
                        }
                    }
                    break;
            }
        }

        public ushort Read16(ushort port)
        {
            byte low = Read8(port);
            byte high = Read8((ushort)(port + 1));
            return (ushort)(low | (high << 8));
        }

        public void Write16(ushort port, ushort value)
        {
            Write8(port, (byte)(value & 0xFF));
            Write8((ushort)(port + 1), (byte)((value >> 8) & 0xFF));
        }

        public uint Read32(ushort port) => Read16(port);
        public void Write32(ushort port, uint value) => Write16(port, (ushort)value);
    }
}
