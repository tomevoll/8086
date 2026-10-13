namespace Amiga500.IO;

public interface ICustomRegisterDevice
{
    ushort ReadRegister(uint regOffset);
    void WriteRegister(uint regOffset, ushort value);
}
