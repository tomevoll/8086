namespace Emulator80386.App.IO
{
    public class SystemControlPort : IIOPortDevice
    {
        public byte PortValue { get; private set; } = 0x02; // A20 gate default enabled

        public byte Read8(ushort port)
        {
            if (port == 0x92)
            {
                return PortValue;
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
                PortValue = value;
            }
        }
    }
}
