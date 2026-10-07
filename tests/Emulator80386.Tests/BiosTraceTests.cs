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

            Assert.True(reachedProtectedMode, "SeaBIOS must enter Protected Mode during POST initialization");
            Assert.True(mb.Cpu.Reg.EIP != 0, "CPU EIP must be active");
        }
    }
}
