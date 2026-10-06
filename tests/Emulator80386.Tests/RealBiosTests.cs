using System;
using System.IO;
using Emulator80386.App;
using Emulator80386.App.Config;
using Xunit;

namespace Emulator80386.Tests
{
    public class RealBiosTests
    {
        public static string? FindBiosFile()
        {
            string fileName = "386-4N-D04A REV2.0.BIN";
            string? dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
                dir = Directory.GetParent(dir)?.FullName;
            }
            return null;
        }

        [Fact]
        public void TestRealAward386BiosExecution()
        {
            string? biosFile = FindBiosFile();
            if (biosFile == null || !File.Exists(biosFile)) return;

            var config = new EmulatorConfig
            {
                RamSizeMB = 16,
                RomPath = biosFile
            };

            var motherboard = new Motherboard(config);
            motherboard.Boot();

            // Reset vector JMP FAR to 0xF000:0xE05B
            motherboard.Cpu.Step();
            Assert.Equal(0xF000, (int)motherboard.Cpu.Reg.CS.Selector);
            Assert.Equal(0xE05B, (int)motherboard.Cpu.Reg.EIP);

            // Execute 10,000 instructions of real Award 386 BIOS POST code
            for (int i = 0; i < 10000; i++)
            {
                if (motherboard.Cpu.Halted) break;
                motherboard.Cpu.Step();
            }

            Assert.True(motherboard.Cpu.Reg.EIP != 0xE05B);
        }
    }
}
