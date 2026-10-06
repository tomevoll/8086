using System.IO;
using Emulator80386.App.Config;
using Xunit;

namespace Emulator80386.Tests
{
    public class ConfigTests
    {
        [Fact]
        public void TestDefaultConfigCreatedWhenFileMissing()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "test_config_missing.ini");
            if (File.Exists(tempFile)) File.Delete(tempFile);

            try
            {
                var config = EmulatorConfig.LoadFromFile(tempFile);
                Assert.Equal(16, config.RamSizeMB);
                Assert.Equal("bios.bin", config.RomPath);
                Assert.Equal("./hdd", config.DriveCFolder);
                Assert.True(File.Exists(tempFile));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void TestParseIniFile()
        {
            string tempFile = Path.GetTempFileName();
            string content = @"
[Memory]
RamSizeMB = 32

[BIOS]
RomPath = custom_bios.bin

[Storage]
DriveCFolder = /tmp/dos_hdd

[Display]
DisplayWidth = 800
DisplayHeight = 600
";
            File.WriteAllText(tempFile, content);

            try
            {
                var config = EmulatorConfig.LoadFromFile(tempFile);
                Assert.Equal(32, config.RamSizeMB);
                Assert.Equal("custom_bios.bin", config.RomPath);
                Assert.Equal("/tmp/dos_hdd", config.DriveCFolder);
                Assert.Equal(800, config.DisplayWidth);
                Assert.Equal(600, config.DisplayHeight);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
