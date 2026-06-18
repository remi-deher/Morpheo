namespace Morpheo.Usb;

/// <summary>USB device class classification based on USB-IF class codes.</summary>
public enum UsbDeviceClass
{
    Unknown = 0,
    HumanInterface = 3,   // HID (keyboard, mouse, etc.) — future
    Printer = 7,   // USB Printer class
    Storage = 8,   // Mass Storage (thumb drives, SSDs)
}
