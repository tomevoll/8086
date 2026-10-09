using System;
using System.Collections.Generic;

namespace Emulator80386.App.IO
{
    public interface IIOPortDevice
    {
        byte Read8(ushort port);
        void Write8(ushort port, byte value);
        ushort Read16(ushort port) => (ushort)(Read8(port) | (Read8((ushort)(port + 1)) << 8));
        void Write16(ushort port, ushort value)
        {
            Write8(port, (byte)(value & 0xFF));
            Write8((ushort)(port + 1), (byte)((value >> 8) & 0xFF));
        }
        uint Read32(ushort port) => (uint)(Read16(port) | (Read16((ushort)(port + 2)) << 16));
        void Write32(ushort port, uint value)
        {
            Write16(port, (ushort)(value & 0xFFFF));
            Write16((ushort)(port + 2), (ushort)((value >> 16) & 0xFFFF));
        }
    }

    public class IOPortBus
    {
        private readonly Dictionary<ushort, IIOPortDevice> _devices = new Dictionary<ushort, IIOPortDevice>();

        public void RegisterDevice(ushort port, IIOPortDevice device)
        {
            _devices[port] = device;
        }

        public byte In8(ushort port)
        {
            if (_devices.TryGetValue(port, out var device))
                return device.Read8(port);
            return 0xFF;
        }

        public void Out8(ushort port, byte value)
        {
            if (_devices.TryGetValue(port, out var device))
                device.Write8(port, value);
        }

        public ushort In16(ushort port)
        {
            if (_devices.TryGetValue(port, out var device))
                return device.Read16(port);
            byte b0 = In8(port);
            byte b1 = In8((ushort)(port + 1));
            return (ushort)(b0 | (b1 << 8));
        }

        public void Out16(ushort port, ushort value)
        {
            if (_devices.TryGetValue(port, out var device))
                device.Write16(port, value);
            else
            {
                Out8(port, (byte)(value & 0xFF));
                Out8((ushort)(port + 1), (byte)((value >> 8) & 0xFF));
            }
        }

        public uint In32(ushort port)
        {
            if (_devices.TryGetValue(port, out var device))
                return device.Read32(port);
            ushort w0 = In16(port);
            ushort w1 = In16((ushort)(port + 2));
            return (uint)(w0 | (w1 << 16));
        }

        public void Out32(ushort port, uint value)
        {
            if (_devices.TryGetValue(port, out var device))
                device.Write32(port, value);
            else
            {
                Out16(port, (ushort)(value & 0xFFFF));
                Out16((ushort)(port + 2), (ushort)((value >> 16) & 0xFFFF));
            }
        }
    }
}
