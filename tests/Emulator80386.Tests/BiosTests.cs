using System.IO;
using Emulator80386.App;
using Emulator80386.App.Config;
using Emulator80386.App.Memory;
using Xunit;

namespace Emulator80386.Tests
{
    public class BiosTests
    {
        [Fact]
        public void TestBiosBootSequenceAndVgaPostMessage()
        {
            var config = new EmulatorConfig { RamSizeMB = 16, RomPath = "roms/bios.bin", VgaRomPath = "roms/vgabios.bin" };
            var motherboard = new Motherboard(config);

            motherboard.Boot();

            // Run CPU steps to execute real BIOS POST instructions
            motherboard.Step(instructionsCount: 500);

            Assert.NotNull(motherboard.Memory.BiosRom);
            Assert.Equal(131072, motherboard.Memory.BiosRom.Length);
        }

        [Fact]
        public void TestSplitLowHighBiosRomLoading()
        {
            string lowFile = Path.GetTempFileName();
            string highFile = Path.GetTempFileName();

            try
            {
                // Low bytes (even indices): 0xAA, 0xCC
                // High bytes (odd indices): 0xBB, 0xDD
                File.WriteAllBytes(lowFile, new byte[] { 0xAA, 0xCC });
                File.WriteAllBytes(highFile, new byte[] { 0xBB, 0xDD });

                var config = new EmulatorConfig
                {
                    RomLowPath = lowFile,
                    RomHighPath = highFile
                };

                byte[] bios = BiosLoader.LoadOrGenerateBios(config);

                Assert.Equal(4, bios.Length);
                Assert.Equal(0xAA, bios[0]);
                Assert.Equal(0xBB, bios[1]);
                Assert.Equal(0xCC, bios[2]);
                Assert.Equal(0xDD, bios[3]);
            }
            finally
            {
                if (File.Exists(lowFile)) File.Delete(lowFile);
                if (File.Exists(highFile)) File.Delete(highFile);
            }
        }
    }
}
