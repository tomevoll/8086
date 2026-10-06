namespace Emulator80386.App.IO
{
    public class Pit8253 : IIOPortDevice
    {
        private ushort _counter0Reload = 0xFFFF;
        private byte _controlByte = 0;

        public byte Read8(ushort port)
        {
            if (port == 0x40)
            {
                return (byte)(_counter0Reload & 0xFF);
            }
            return 0x00;
        }

        public void Write8(ushort port, byte value)
        {
            if (port == 0x43)
            {
                _controlByte = value;
            }
            else if (port == 0x40)
            {
                _counter0Reload = (ushort)((_counter0Reload >> 8) | (value << 8));
            }
        }
    }
}
