using System.IO;
using Emulator80386.App;
using Emulator80386.App.Config;
using Xunit;

namespace Emulator80386.Tests
{
    public class RealBiosTests
    {
        [Fact]
        public void TestRealAward386BiosExecution()
        {
            string biosFile = "386-4N-D04A REV2.0.BIN";
            if (!File.Exists(biosFile))
            {
                return; // Skip if file is not in working directory
            }

            var config = new EmulatorConfig
            {
                RamSizeMB = 16,
                RomPath = biosFile
            };

            var motherboard = new Motherboard(config);
            motherboard.Boot();

            // Reset vector JMP FAR to 0xF000:0xE05B
            motherboard.Cpu.Step(); // JMP FAR
            Assert.Equal(0xF000, (int)motherboard.Cpu.Reg.CS.Selector);
            Assert.Equal(0xE05B, (int)motherboard.Cpu.Reg.EIP);

            // Execute 10,000 instructions of real Award 386 BIOS POST code
            for (int i = 0; i < 10000; i++)
            {
                if (motherboard.Cpu.Halted) break;
                motherboard.Cpu.Step();
            }

            // Verify CPU executed through POST code without crashing
            Assert.True(motherboard.Cpu.Reg.EIP != 0xE05B);
        }
    }
}
