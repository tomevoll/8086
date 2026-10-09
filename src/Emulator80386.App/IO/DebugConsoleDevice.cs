using System;
using System.Text;

namespace Emulator80386.App.IO
{
    public class DebugConsoleDevice : IIOPortDevice
    {
        public StringBuilder Log { get; } = new StringBuilder();

        public byte Read8(ushort port) => 0xE9;

        public ushort Read16(ushort port) => 0xE9E9;

        public uint Read32(ushort port) => 0xE9E9E9E9;

        public void Write8(ushort port, byte value)
        {
            if (port == 0x0402 || port == 0xE9)
            {
                char c = (char)value;
                Log.Append(c);
            }
        }

        public void Write16(ushort port, ushort value)
        {
            Write8(port, (byte)(value & 0xFF));
            Write8(port, (byte)((value >> 8) & 0xFF));
        }

        public void Write32(ushort port, uint value)
        {
            Write8(port, (byte)(value & 0xFF));
            Write8(port, (byte)((value >> 8) & 0xFF));
            Write8(port, (byte)((value >> 16) & 0xFF));
            Write8(port, (byte)((value >> 24) & 0xFF));
        }
    }
}
