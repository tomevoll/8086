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

            var config = new EmulatorConfig { RamSizeMB = 16, RomPath = biosPath };
            var mb = new Motherboard(config);
            mb.Boot();

            // Run initial 20,000 steps to verify CPU boot execution
            for (int step = 0; step < 20000; step++)
            {
                mb.Cpu.Step();
            }

            Assert.True(mb.Cpu.Reg.EIP != 0, "CPU EIP should be active during BIOS execution");
            Assert.True(mb.Cpu.Reg.ProtectedMode, "SeaBIOS enters Protected Mode during boot initialization");
        }
    }
}
