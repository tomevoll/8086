using System;
using System.IO;
using System.Text;
using Emulator80386.App;
using Emulator80386.App.Config;
using Emulator80386.App.CPU;
using Xunit;

namespace Emulator80386.Tests
{
    public class BiosTraceTests
    {
        [Fact]
        public void TraceAwardBiosBoot()
        {
            string? biosFile = RealBiosTests.FindBiosFile();
            if (biosFile == null || !File.Exists(biosFile)) return;

            var config = new EmulatorConfig { RamSizeMB = 16, RomPath = biosFile };
            var mb = new Motherboard(config);
            mb.Boot();

            var sb = new StringBuilder();

            for (int step = 0; step < 10000; step++)
            {
                uint ip = mb.Cpu.Reg.EIP;
                ushort cs = mb.Cpu.Reg.CS.Selector;
                uint linear = mb.Cpu.LinearAddress(mb.Cpu.Reg.CS, ip);
                byte op = mb.Memory.Read8(linear);

                if (step >= 9950)
                {
                    sb.AppendLine($"[{step:D5}] CS:IP={cs:X4}:{ip:X4} (Linear {linear:X8}) Op={op:X2} AX={mb.Cpu.Reg.AX:X4} DX={mb.Cpu.Reg.DX:X4} ZF={(mb.Cpu.Reg.GetFlag(EFlags.ZF) ? 1 : 0)}");
                }

                if (mb.Cpu.Halted) break;
                mb.Cpu.Step();
            }

            File.WriteAllText("/tmp/bios_trace.txt", sb.ToString());
        }
    }
}
