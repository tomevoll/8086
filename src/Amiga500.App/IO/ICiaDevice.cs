namespace Amiga500.IO;

public interface ICiaDevice
{
    byte ReadRegister(uint address);
    void WriteRegister(uint address, byte value);
}
