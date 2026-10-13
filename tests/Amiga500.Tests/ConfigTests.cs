using System.IO;
using Amiga500.Config;
using Xunit;

namespace Amiga500.Tests;

public class ConfigTests
{
    [Fact]
    public void DefaultConfig_HasCorrectAmiga500Defaults()
    {
        var config = new EmulatorConfig();
        Assert.Equal("roms/kick13.rom", config.KickstartRomPath);
        Assert.Equal(512, config.ChipRamKB);
        Assert.Equal(VideoStandard.PAL, config.VideoStandard);
        Assert.Equal(4, config.FloppyPaths.Length);
        Assert.True(config.EnableAudio);
    }

    [Fact]
    public void SaveAndLoadConfig_PersistsValues()
    {
        var config = new EmulatorConfig
        {
            KickstartRomPath = "roms/test_kick.rom",
            ChipRamKB = 1024,
            VideoStandard = VideoStandard.NTSC
        };
        config.Save();

        var loaded = EmulatorConfig.Load();
        Assert.Equal("roms/test_kick.rom", loaded.KickstartRomPath);
        Assert.Equal(1024, loaded.ChipRamKB);
        Assert.Equal(VideoStandard.NTSC, loaded.VideoStandard);

        if (File.Exists("config.json"))
        {
            File.Delete("config.json");
        }
    }
}
