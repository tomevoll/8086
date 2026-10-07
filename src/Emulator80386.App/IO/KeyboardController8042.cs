using System;
using System.Collections.Generic;

namespace Emulator80386.App.IO
{
    public class KeyboardController8042 : IIOPortDevice
    {
        private readonly Queue<byte> _buffer = new Queue<byte>();
        private byte[] _ram = new byte[32];
        private byte _commandByte = 0x47; // Default: IRQ1 enabled, translation enabled
        private bool _expectingCommandData = false;
        private byte _lastCommand = 0;
        public bool A20Enabled { get; private set; } = true;

        public Action? OnCpuReset { get; set; }

        public KeyboardController8042()
        {
            _ram[0] = _commandByte;
            _ram[0x18] = 0x00; // Password / Security status cleared
        }

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
                    if ((_lastCommand >= 0x60 && _lastCommand <= 0x7F))
                    {
                        int index = _lastCommand & 0x1F;
                        _ram[index] = value;
                        if (index == 0) _commandByte = value;
                    }
                    else if (_lastCommand == 0xD1) // Write output port
                    {
                        A20Enabled = (value & 0x02) != 0;
                        if ((value & 0x01) == 0)
                        {
                            OnCpuReset?.Invoke();
                        }
                    }
                    else if (_lastCommand == 0xD2 || _lastCommand == 0xD3)
                    {
                        _buffer.Enqueue(value);
                    }
                    else if (_lastCommand == 0xD4) // Mouse command
                    {
                        _buffer.Enqueue(0xFA); // ACK
                    }
                    else if (_lastCommand == 0xED || _lastCommand == 0xF3)
                    {
                        _buffer.Enqueue(0xFA); // ACK
                    }
                }
                else
                {
                    // Keyboard Device commands
                    if (value == 0xFF) // Reset
                    {
                        _buffer.Enqueue(0xFA); // ACK
                        _buffer.Enqueue(0xAA); // Self test passed
                    }
                    else if (value == 0xF2) // Read ID
                    {
                        _buffer.Enqueue(0xFA); // ACK
                        _buffer.Enqueue(0xAB);
                        _buffer.Enqueue(0x83);
                    }
                    else if (value == 0xED || value == 0xF3) // Set LEDs / Set Typematic Rate
                    {
                        _expectingCommandData = true;
                        _lastCommand = value;
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

                if (value >= 0x20 && value <= 0x3F) // Read RAM Byte
                {
                    int index = value & 0x1F;
                    _buffer.Enqueue(_ram[index]);
                }
                else if (value >= 0x60 && value <= 0x7F) // Write RAM Byte
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
                else if (value == 0xA1) // Firmware Revision
                {
                    _buffer.Enqueue(0x90);
                }
                else if (value == 0xA7) // Disable Mouse
                {
                }
                else if (value == 0xA8) // Enable Mouse
                {
                }
                else if (value == 0xA9) // Mouse Interface Test
                {
                    _buffer.Enqueue(0x00); // OK
                }
                else if (value == 0xAD) // Disable Keyboard
                {
                }
                else if (value == 0xAE) // Enable Keyboard
                {
                }
                else if (value == 0xC0) // Read Input Port
                {
                    _buffer.Enqueue(0x80);
                }
                else if (value == 0xD0) // Read Output Port
                {
                    _buffer.Enqueue((byte)(0x01 | (A20Enabled ? 0x02 : 0x00)));
                }
                else if (value == 0xD1 || value == 0xD2 || value == 0xD3 || value == 0xD4)
                {
                    _expectingCommandData = true;
                }
                else if (value == 0xFE || (value & 0xF0) == 0xF0) // System Reset Pulse
                {
                    OnCpuReset?.Invoke();
                }
            }
        }
    }
}
