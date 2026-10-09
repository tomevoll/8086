using System;
using System.IO;
using Emulator80386.App;
using Emulator80386.App.Config;
using Emulator80386.App.Memory;
using Xunit;

namespace Emulator80386.Tests
{
    public class BiosTraceTests
    {
        [Fact]
        public void VerifyRealBiosPostExecution()
        {
            string biosPath = BiosLoader.ResolveRomPath("bios.bin");
            Assert.True(File.Exists(biosPath), $"Real BIOS binary 'bios.bin' must exist at {biosPath}");

            var config = new EmulatorConfig { RamSizeMB = 64, RomPath = biosPath };
            var mb = new Motherboard(config);
            mb.Boot();

            bool reachedProtectedMode = false;
            for (int step = 0; step < 50000; step++)
            {
                mb.Cpu.Step();
                if (mb.Cpu.Reg.ProtectedMode)
                {
                    reachedProtectedMode = true;
                    break;
                }
            }

            uint postVar = mb.Memory.Read32(0x000F610C);
            Console.WriteLine($"Value at 0x000F610C in ROM = 0x{postVar:X8}");
            Assert.True(reachedProtectedMode, "SeaBIOS must enter Protected Mode during POST initialization");

            var mb2 = new Motherboard(config);
            mb2.Boot();
            for (int step = 0; step < 100000; step++)
            {
                mb2.Cpu.Step();
            }

            Assert.True(mb2.Cpu.Reg.EIP != 0, "CPU instruction execution must advance cleanly");
        }
    }
}
