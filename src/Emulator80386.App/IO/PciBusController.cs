using System;

namespace Emulator80386.App.IO
{
    public class PciBusController : IIOPortDevice
    {
        public uint AddressRegister { get; private set; }

        private readonly byte[] _dev0Config = new byte[256]; // i440FX
        private readonly byte[] _dev1Config = new byte[256]; // PIIX3
        private readonly byte[] _dev2Config = new byte[256]; // VGA

        public PciBusController()
        {
            // Dev 0: i440FX Host Bridge (0x8086:0x1237)
            _dev0Config[0x00] = 0x86; _dev0Config[0x01] = 0x80;
            _dev0Config[0x02] = 0x37; _dev0Config[0x03] = 0x12;
            _dev0Config[0x0A] = 0x00; _dev0Config[0x0B] = 0x06; // Host Bridge Class 0x0600
            _dev0Config[0x0E] = 0x00;

            // Dev 1: PIIX3 ISA Bridge (0x8086:0x7000)
            _dev1Config[0x00] = 0x86; _dev1Config[0x01] = 0x80;
            _dev1Config[0x02] = 0x00; _dev1Config[0x03] = 0x70;
            _dev1Config[0x0A] = 0x01; _dev1Config[0x0B] = 0x06; // ISA Bridge Class 0x0601
            _dev1Config[0x0E] = 0x00;

            // Dev 2: QEMU Standard VGA (0x1234:0x1111)
            _dev2Config[0x00] = 0x34; _dev2Config[0x01] = 0x12;
            _dev2Config[0x02] = 0x11; _dev2Config[0x03] = 0x11;
            _dev2Config[0x0A] = 0x00; _dev2Config[0x0B] = 0x03; // VGA Display Controller Class 0x0300
            _dev2Config[0x0E] = 0x00;
        }

        public byte Read8(ushort port)
        {
            if (port >= 0x0CF8 && port <= 0x0CFB)
            {
                int shift = (port - 0x0CF8) * 8;
                return (byte)((AddressRegister >> shift) & 0xFF);
            }

            if (port >= 0x0CFC && port <= 0x0CFF)
            {
                if ((AddressRegister & 0x80000000) == 0) return 0xFF;

                uint bus = (AddressRegister >> 16) & 0xFF;
                uint dev = (AddressRegister >> 11) & 0x1F;
                uint fn = (AddressRegister >> 8) & 0x07;
                uint reg = (AddressRegister & 0xFC) | (uint)(port - 0x0CFC);

                if (bus == 0 && fn == 0 && reg < 256)
                {
                    if (dev == 0) return _dev0Config[reg];
                    if (dev == 1) return _dev1Config[reg];
                    if (dev == 2) return _dev2Config[reg];
                }
                return 0xFF;
            }
            return 0xFF;
        }

        public ushort Read16(ushort port)
        {
            byte b0 = Read8(port);
            byte b1 = Read8((ushort)(port + 1));
            return (ushort)(b0 | (b1 << 8));
        }

        public uint Read32(ushort port)
        {
            if (port == 0x0CF8) return AddressRegister;
            if (port >= 0x0CFC && port <= 0x0CFF)
            {
                byte b0 = Read8(0x0CFC);
                byte b1 = Read8(0x0CFD);
                byte b2 = Read8(0x0CFE);
                byte b3 = Read8(0x0CFF);
                return (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
            }
            return 0xFFFFFFFF;
        }

        public Memory.MemoryBus? Memory { get; set; }

        public void Write8(ushort port, byte value)
        {
            if (port >= 0x0CF8 && port <= 0x0CFB)
            {
                int shift = (port - 0x0CF8) * 8;
                uint mask = ~(0xFFu << shift);
                AddressRegister = (AddressRegister & mask) | ((uint)value << shift);
                return;
            }

            if (port >= 0x0CFC && port <= 0x0CFF)
            {
                uint bus = (AddressRegister >> 16) & 0xFF;
                uint dev = (AddressRegister >> 11) & 0x1F;
                uint fn = (AddressRegister >> 8) & 0x07;
                uint reg = (AddressRegister & 0xFC) | (uint)(port - 0x0CFC);

                if (bus == 0 && fn == 0 && reg < 256)
                {
                    if (dev == 0)
                    {
                        _dev0Config[reg] = value;
                        if (reg >= 0x59 && reg <= 0x5F && Memory != null)
                        {
                            UpdatePamShadow(reg, value);
                        }
                    }
                    else if (dev == 1) _dev1Config[reg] = value;
                    else if (dev == 2) _dev2Config[reg] = value;
                }
            }
        }

        public void Write16(ushort port, ushort value)
        {
            Write8(port, (byte)(value & 0xFF));
            Write8((ushort)(port + 1), (byte)((value >> 8) & 0xFF));
        }

        public void Write32(ushort port, uint value)
        {
            if (port == 0x0CF8)
            {
                AddressRegister = value;
            }
            else if (port >= 0x0CFC && port <= 0x0CFF)
            {
                Write8(0x0CFC, (byte)(value & 0xFF));
                Write8(0x0CFD, (byte)((value >> 8) & 0xFF));
                Write8(0x0CFE, (byte)((value >> 16) & 0xFF));
                Write8(0x0CFF, (byte)((value >> 24) & 0xFF));
            }
        }

        private void UpdatePamShadow(uint reg, byte value)
        {
            if (Memory == null) return;

            if (reg == 0x59)
            {
                // PAM0: bits 5:4 control 0xF0000 - 0xFFFFF (segments 12..15), bits 1:0 control 0xE0000 - 0xEFFFF (segments 8..11)
                bool r = (value & 0x10) != 0 || (value & 0x01) != 0;
                bool w = (value & 0x20) != 0 || (value & 0x02) != 0;
                for (int s = 8; s <= 15; s++)
                {
                    Memory.ShadowRead[s] = r;
                    Memory.ShadowWrite[s] = w;
                }
            }
            else if (reg >= 0x5A && reg <= 0x5F)
            {
                // PAM1..PAM6: control 0xC0000 - 0xEFFFF in pairs of 16KB segments
                int baseSeg = (int)((reg - 0x5A) * 2);
                bool r0 = (value & 0x01) != 0;
                bool w0 = (value & 0x02) != 0;
                bool r1 = (value & 0x10) != 0;
                bool w1 = (value & 0x20) != 0;

                Memory.ShadowRead[baseSeg] = r0;
                Memory.ShadowWrite[baseSeg] = w0;
                Memory.ShadowRead[baseSeg + 1] = r1;
                Memory.ShadowWrite[baseSeg + 1] = w1;
            }
        }
    }
}
