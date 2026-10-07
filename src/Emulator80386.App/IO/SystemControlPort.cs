namespace Emulator80386.App.IO
{
    public class SystemControlPort : IIOPortDevice
    {
        public byte Port92Value { get; private set; } = 0x02; // A20 gate default enabled
        public byte Port61Value { get; private set; } = 0x00;
        private byte _refreshToggle = 0x10;

        public byte Read8(ushort port)
        {
            if (port == 0x92)
            {
                return Port92Value;
            }
            if (port == 0x61)
            {
                _refreshToggle ^= 0x10; // Toggle RAM refresh bit 4 on read
                return (byte)((Port61Value & ~0x10) | _refreshToggle);
            }
            if (port == 0xDF || port == 0xEE)
            {
                return 0x00; // Chipset A20 ready status
            }
            return 0x00;
        }

        public void Write8(ushort port, byte value)
        {
            if (port == 0x92)
            {
                Port92Value = value;
            }
            else if (port == 0x61)
            {
                Port61Value = value;
            }
        }

        public ushort Read16(ushort port) => Read8(port);
        public void Write16(ushort port, ushort value) => Write8(port, (byte)value);
        public uint Read32(ushort port) => Read8(port);
        public void Write32(ushort port, uint value) => Write8(port, (byte)value);
    }
}
