namespace Emulator80386.App.IO
{
    public class Pic8259 : IIOPortDevice
    {
        private readonly bool _isSlave;
        public byte Mask { get; private set; } = 0xFF;
        public byte VectorOffset { get; private set; }

        public Pic8259(bool isSlave)
        {
            _isSlave = isSlave;
            VectorOffset = (byte)(isSlave ? 0x70 : 0x08);
        }

        public byte Read8(ushort port)
        {
            if (port == 0x21 || port == 0xA1)
            {
                return Mask;
            }
            return 0x00;
        }

        public void Write8(ushort port, byte value)
        {
            if (port == 0x21 || port == 0xA1)
            {
                Mask = value;
            }
            else if (port == 0x20 || port == 0xA0)
            {
                if ((value & 0x10) != 0) // ICW1
                {
                    Mask = 0x00;
                }
            }
        }
    }
}
