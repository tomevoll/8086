using System;
using System.Collections.Generic;

namespace Emulator80386.App.IO
{
    public class KeyboardController8042 : IIOPortDevice
    {
        private readonly Queue<byte> _buffer = new Queue<byte>();
        private byte _commandByte = 0x47; // Default: IRQ1 enabled, translation enabled
        private bool _expectingCommandData = false;
        private byte _lastCommand = 0;
        public bool A20Enabled { get; private set; } = true;

        public Action? OnCpuReset { get; set; }

        public void EnqueueScancode(byte scancode)
        {
            _buffer.Enqueue(scancode);
        }

        public byte Read8(ushort port)
        {
            if (port == 0x60) // Data Port
            {
                if (_buffer.Count > 0)
                {
                    return _buffer.Dequeue();
                }
                return 0x00;
            }
            else if (port == 0x64) // Status Port
            {
                byte status = 0x1C; // System flag set, keyboard enabled
                if (_buffer.Count > 0)
                {
                    status |= 0x01; // Output buffer full
                }
                return status;
            }
            return 0xFF;
        }

        public void Write8(ushort port, byte value)
        {
            if (port == 0x60) // Data Port
            {
                if (_expectingCommandData)
                {
                    _expectingCommandData = false;
                    if (_lastCommand == 0x60)
                    {
                        _commandByte = value;
                    }
                    else if (_lastCommand == 0xD1) // Write output port
                    {
                        A20Enabled = (value & 0x02) != 0;
                        if ((value & 0x01) == 0)
                        {
                            OnCpuReset?.Invoke();
                        }
                    }
                }
                else
                {
                    if (value == 0xFF) // Reset
                    {
                        _buffer.Enqueue(0xFA); // ACK
                        _buffer.Enqueue(0xAA); // Self test passed
                    }
                    else if (value == 0xF4) // Enable scanning
                    {
                        _buffer.Enqueue(0xFA); // ACK
                    }
                    else
                    {
                        _buffer.Enqueue(0xFA); // ACK
                    }
                }
            }
            else if (port == 0x64) // Command Port
            {
                _lastCommand = value;
                if (value == 0xFE || (value & 0xF0) == 0xF0) // System Reset Pulse
                {
                    OnCpuReset?.Invoke();
                }
                else if (value == 0x20) // Read Controller Command Byte
                {
                    _buffer.Enqueue(_commandByte);
                }
                else if (value == 0x60) // Write Controller Command Byte
                {
                    _expectingCommandData = true;
                }
                else if (value == 0xAA) // Controller Self Test
                {
                    _buffer.Enqueue(0x55); // Passed
                }
                else if (value == 0xAB) // Keyboard Interface Test
                {
                    _buffer.Enqueue(0x00); // OK
                }
                else if (value == 0xAD) // Disable Keyboard
                {
                }
                else if (value == 0xAE) // Enable Keyboard
                {
                }
                else if (value == 0xD1) // Write Output Port
                {
                    _expectingCommandData = true;
                }
            }
        }
    }
}
