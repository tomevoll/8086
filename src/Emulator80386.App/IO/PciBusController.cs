using System;

namespace Emulator80386.App.IO
{
    public class PciBusController : IIOPortDevice
    {
        public uint AddressRegister { get; private set; }

        public byte Read8(ushort port) => 0xFF;

        public ushort Read16(ushort port) => 0xFFFF;

        public uint Read32(ushort port)
        {
            if (port == 0x0CF8)
            {
                return AddressRegister;
            }
            if (port == 0x0CFC)
            {
                return 0xFFFFFFFF; // No PCI devices attached -> return 0xFFFFFFFF
            }
            return 0xFFFFFFFF;
        }

        public void Write8(ushort port, byte value) { }
        public void Write16(ushort port, ushort value) { }

        public void Write32(ushort port, uint value)
        {
            if (port == 0x0CF8)
            {
                AddressRegister = value;
            }
        }
    }
}
