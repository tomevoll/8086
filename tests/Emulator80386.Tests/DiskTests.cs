using System.IO;
using Emulator80386.App.Disk;
using Xunit;

namespace Emulator80386.Tests
{
    public class DiskTests
    {
        [Fact]
        public void TestBootSecCreatedInFolderDrive()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_hdd_" + Path.GetRandomFileName());
            try
            {
                var disk = new FolderDiskController(tempDir);
                Assert.True(Directory.Exists(tempDir));
                Assert.True(File.Exists(disk.BootSecPath));

                byte[] bootSec = disk.ReadBootSector();
                Assert.Equal(512, bootSec.Length);
                Assert.Equal(0x55, bootSec[510]);
                Assert.Equal(0xAA, bootSec[511]);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void TestWriteAndReadBootSector()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "test_hdd_" + Path.GetRandomFileName());
            try
            {
                var disk = new FolderDiskController(tempDir);
                byte[] customBootSec = new byte[512];
                customBootSec[0] = 0x90; // NOP
                customBootSec[510] = 0x55;
                customBootSec[511] = 0xAA;

                disk.WriteBootSector(customBootSec);

                byte[] readData = disk.ReadBootSector();
                Assert.Equal(0x90, readData[0]);
                Assert.Equal(0x55, readData[510]);
                Assert.Equal(0xAA, readData[511]);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
