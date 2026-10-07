using System;

namespace Emulator80386.App.IO
{
    public class PostAndDmaController : IIOPortDevice
    {
        public byte LastPostCode { get; private set; }
        private readonly byte[] _dmaPageRegs = new byte[16];
        private readonly byte[] _dma1Regs = new byte[16];
        private readonly byte[] _dma2Regs = new byte[32];

        public byte Read8(ushort port)
        {
            if (port == 0x80)
            {
                return LastPostCode;
            }

            // DMA 1 Primary (0x00 - 0x0F)
            if (port <= 0x0F)
            {
                if (port == 0x08) return 0x00; // Status register
                return _dma1Regs[port];
            }

            // DMA Page Registers (0x81 - 0x8F)
            if (port >= 0x80 && port <= 0x8F)
            {
                return _dmaPageRegs[port - 0x80];
            }

            // DMA 2 Secondary (0xC0 - 0xDE)
            if (port >= 0xC0 && port <= 0xDE)
            {
                int index = (port - 0xC0) / 2;
                if (index >= 0 && index < _dma2Regs.Length)
                {
                    return _dma2Regs[index];
                }
            }

            return 0x00;
        }

        public void Write8(ushort port, byte value)
        {
            if (port == 0x80)
            {
                LastPostCode = value;
                return;
            }

            if (port <= 0x0F)
            {
                _dma1Regs[port] = value;
                return;
            }

            if (port >= 0x80 && port <= 0x8F)
            {
                _dmaPageRegs[port - 0x80] = value;
                return;
            }

            if (port >= 0xC0 && port <= 0xDE)
            {
                int index = (port - 0xC0) / 2;
                if (index >= 0 && index < _dma2Regs.Length)
                {
                    _dma2Regs[index] = value;
                }
            }
        }

        public ushort Read16(ushort port) => Read8(port);
        public void Write16(ushort port, ushort value) => Write8(port, (byte)value);
        public uint Read32(ushort port) => Read8(port);
        public void Write32(ushort port, uint value) => Write8(port, (byte)value);
    }
}
