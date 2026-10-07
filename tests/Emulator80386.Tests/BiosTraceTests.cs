using System;
using System.IO;
using Emulator80386.App;
using Emulator80386.App.Config;
using Xunit;

namespace Emulator80386.Tests
{
    public class BiosTraceTests
    {
        [Fact]
        public void TraceSeaBiosSteps27800To27815()
        {
            string seaBiosPath = "/usr/share/seabios/bios.bin";
            if (!File.Exists(seaBiosPath)) return;

            var config = new EmulatorConfig { RamSizeMB = 16, RomPath = seaBiosPath };
            var mb = new Motherboard(config);
            mb.Boot();

            for (int step = 0; step < 27816; step++)
            {
                if (step >= 27800)
                {
                    uint ip = mb.Cpu.Reg.EIP;
                    ushort cs = mb.Cpu.Reg.CS.Selector;
                    uint linear = mb.Cpu.LinearAddress(mb.Cpu.Reg.CS, ip);
                    byte op = mb.Memory.Read8(linear);

                    Console.WriteLine($"[{step:D5}] CS:IP={cs:X4}:{ip:X8} (Lin {linear:X8}) Op={op:X2} EAX={mb.Cpu.Reg.EAX:X8} EBX={mb.Cpu.Reg.EBX:X8} ECX={mb.Cpu.Reg.ECX:X8} EDX={mb.Cpu.Reg.EDX:X8} ESP={mb.Cpu.Reg.ESP:X8}");
                }

                mb.Cpu.Step();
            }
        }
    }
}
