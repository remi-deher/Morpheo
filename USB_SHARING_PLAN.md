# Morpheo USB Device Sharing Implementation Plan

**Version:** 1.0  
**Date:** 2026-06-08  
**Scope:** USB Storage & Printing over Network (Windows + Linux)  
**Status:** Ready for Implementation

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Phase 1: USB Storage](#phase-1-usb-storage-weeks-1-3)
4. [Phase 2: USB Printing](#phase-2-usb-printing-weeks-4-5)
5. [Phase 3: Integration & Security](#phase-3-integration--security-weeks-6-8)
6. [Dependencies](#dependencies)
7. [Testing Strategy](#testing-strategy)
8. [Security & Audit](#security--audit)
9. [Implementation Checklist](#implementation-checklist)

---

## Overview

### Goal

Implement USB device virtualization in Morpheo to allow:
- **USB Storage** devices (thumb drives, external SSDs, card readers) to be accessed remotely as virtual blob stores
- **USB Printers** (any model, any brand) to be accessed remotely as virtual print queues

### Approach

**Not** a USB-level redirect (like RDP), but **device capability virtualization**:
- Enumerate physical USB devices
- Capture their data flows (file I/O, print jobs)
- Transport via HTTP/WebSocket + standard Morpheo encryption/auth
- Expose as Morpheo services (IMorpheoBlobStore, IPrintGateway)

### Key Constraints

✅ Windows + Linux support (macOS future)  
✅ No payload size limits  
✅ Full authentication & audit logging  
✅ Any printer model (via RAW protocol)  
✅ Security: RBAC per device + API key validation

---

## Architecture

### High-Level Data Flow

```
┌──────────────────┐         ┌──────────────────┐
│  Local USB Device│         │  Local USB Device│
│   (Storage)      │         │    (Printer)     │
└────────┬─────────┘         └────────┬─────────┘
         │                            │
         │ libusb/WinUSB             │ libusb/WinUSB
         │                            │
    ┌────▼────────────────────────────▼────┐
    │  Morpheo.Usb Service Layer           │
    │  - IUsbDeviceEnumerator              │
    │  - IUsbStorageAdapter                │
    │  - IUsbPrinterAdapter                │
    │  - IUsbAuditLogger                   │
    └────┬────────────────────────────┬────┘
         │                            │
    ┌────▼───────────────────────────▼────┐
    │  Morpheo API Endpoints              │
    │  /api/usb/devices                   │
    │  /api/usb/storage/{id}/*            │
    │  /api/usb/printers                  │
    │  /api/usb/print/{id}                │
    └────┬────────────────────────────┬────┘
         │                            │
         │ HTTPS + Auth               │
         │ + Audit logging            │
         │                            │
    ┌────▼───────────────────────────▼────┐
    │  Remote Clients                    │
    │  (via SMB mount / print queue)      │
    └────────────────────────────────────┘
```

### Project Structure

```
Morpheo.Usb/
├── Core/
│   ├── IUsbDeviceEnumerator.cs
│   ├── UsbDeviceInfo.cs
│   ├── UsbDeviceClass.cs
│   ├── IUsbAccessPolicy.cs
│   └── UsbAuditEvent.cs
├── Storage/
│   ├── IUsbStorageAdapter.cs
│   ├── RemoteUsbBlobStore.cs
│   └── UsbStorageDevice.cs
├── Printing/
│   ├── IUsbPrinterAdapter.cs
│   ├── RemoteUsbPrinter.cs
│   └── UsbPrinterDevice.cs
├── Platform/
│   ├── IUsbDriver.cs
│   ├── Windows/
│   │   ├── WinUsbDriver.cs
│   │   ├── WinUsbEnumerator.cs
│   │   └── WindowsPrinterAdapter.cs
│   └── Linux/
│       ├── LibUsbDriver.cs
│       ├── LibUsbEnumerator.cs
│       └── LinuxPrinterAdapter.cs
├── Audit/
│   ├── IUsbAuditLogger.cs
│   └── UsbAuditLogger.cs
└── UsbSharingOptions.cs
```

---

## Phase 1: USB Storage (Weeks 1-3)

### 1.1 Core Interfaces & Models

**File:** `Morpheo.Usb/Core/UsbDeviceClass.cs`

```csharp
namespace Morpheo.Usb;

public enum UsbDeviceClass
{
    Unknown = 0,
    Storage = 8,           // Mass Storage
    Printer = 7,           // Printer
    HumanInterface = 3,    // HID (future)
}
```

**File:** `Morpheo.Usb/Core/UsbDeviceInfo.cs`

```csharp
namespace Morpheo.Usb;

/// <summary>
/// Metadata about a connected USB device discovered by enumeration.
/// </summary>
public class UsbDeviceInfo
{
    /// <summary>Unique identifier (bus:device on Linux, device path on Windows)</summary>
    public required string Id { get; set; }
    
    /// <summary>USB Vendor ID (0x1234)</summary>
    public required string VendorId { get; set; }
    
    /// <summary>USB Product ID (0x5678)</summary>
    public required string ProductId { get; set; }
    
    /// <summary>Manufacturer name from USB descriptor</summary>
    public string? Manufacturer { get; set; }
    
    /// <summary>Product name from USB descriptor</summary>
    public string? ProductName { get; set; }
    
    /// <summary>Device class (Storage, Printer, etc.)</summary>
    public required UsbDeviceClass DeviceClass { get; set; }
    
    /// <summary>Total capacity in bytes (Storage only)</summary>
    public long? CapacityBytes { get; set; }
    
    /// <summary>Whether device is read-only</summary>
    public bool IsReadOnly { get; set; }
    
    /// <summary>Serial number if available</summary>
    public string? SerialNumber { get; set; }
    
    /// <summary>When device was first discovered</summary>
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}
```

**File:** `Morpheo.Usb/Core/IUsbDeviceEnumerator.cs`

```csharp
namespace Morpheo.Usb;

/// <summary>
/// Platform-independent interface for discovering connected USB devices.
/// Implementations: WinUsbEnumerator (Windows), LibUsbEnumerator (Linux)
/// </summary>
public interface IUsbDeviceEnumerator
{
    /// <summary>Enumerate all connected USB devices.</summary>
    Task<IReadOnlyList<UsbDeviceInfo>> EnumerateAsync(CancellationToken ct = default);
    
    /// <summary>Enumerate devices matching a specific class.</summary>
    Task<IReadOnlyList<UsbDeviceInfo>> EnumerateByClassAsync(
        UsbDeviceClass deviceClass, 
        CancellationToken ct = default);
    
    /// <summary>Get detailed info about a single device.</summary>
    Task<UsbDeviceInfo?> GetDeviceAsync(string deviceId, CancellationToken ct = default);
}
```

**File:** `Morpheo.Usb/Core/IUsbAccessPolicy.cs`

```csharp
namespace Morpheo.Usb;

/// <summary>
/// Authorization policy: who can access which USB devices.
/// Integrated with Morpheo auth system.
/// </summary>
public interface IUsbAccessPolicy
{
    /// <summary>Check if a principal (user/service) can access a specific device.</summary>
    Task<bool> CanAccessDeviceAsync(
        string principalId,           // from JWT/auth context
        UsbDeviceInfo device,
        UsbAccessType accessType,    // Read, Write, Execute
        CancellationToken ct = default);
}

public enum UsbAccessType
{
    Read = 1,
    Write = 2,
    Execute = 4, // For some devices
}
```

**File:** `Morpheo.Usb/Audit/UsbAuditEvent.cs`

```csharp
namespace Morpheo.Usb.Audit;

/// <summary>Logged whenever a USB device is accessed.</summary>
public class UsbAuditEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    public required string PrincipalId { get; set; }      // user/service making request
    public required string DeviceId { get; set; }         // USB device being accessed
    public required string Operation { get; set; }        // "Read", "Write", "Mount", etc.
    public required UsbAccessType AccessType { get; set; }
    
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    
    public long? BytesTransferred { get; set; }           // for data operations
    public TimeSpan? Duration { get; set; }
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
}
```

**File:** `Morpheo.Usb/Audit/IUsbAuditLogger.cs`

```csharp
namespace Morpheo.Usb.Audit;

public interface IUsbAuditLogger
{
    /// <summary>Log a USB device access event.</summary>
    Task LogAccessAsync(UsbAuditEvent @event, CancellationToken ct = default);
    
    /// <summary>Query audit log with filtering.</summary>
    Task<IReadOnlyList<UsbAuditEvent>> QueryAsync(
        string? principalId = null,
        string? deviceId = null,
        DateTimeOffset? since = null,
        int limit = 1000,
        CancellationToken ct = default);
}
```

### 1.2 Platform-Independent Adapter Interface

**File:** `Morpheo.Usb/Storage/IUsbStorageAdapter.cs`

```csharp
namespace Morpheo.Usb.Storage;

/// <summary>
/// Adapter that wraps a USB Storage device and exposes it as IMorpheoBlobStore.
/// </summary>
public interface IUsbStorageAdapter
{
    /// <summary>Create a virtual blob store from a physical USB device.</summary>
    Task<IMorpheoBlobStore> CreateVirtualStorageAsync(
        UsbDeviceInfo device,
        IUsbAuditLogger auditLogger,
        IUsbAccessPolicy accessPolicy,
        CancellationToken ct = default);
}
```

**File:** `Morpheo.Usb/Storage/RemoteUsbBlobStore.cs`

```csharp
namespace Morpheo.Usb.Storage;

using Morpheo.Sdk.Blobs;
using Morpheo.Usb.Audit;

/// <summary>
/// Implements IMorpheoBlobStore by reading/writing a USB Storage device.
/// Maps blob paths to files on device (first partition).
/// </summary>
public class RemoteUsbBlobStore : IMorpheoBlobStore
{
    private readonly UsbDeviceInfo _device;
    private readonly IUsbAuditLogger _auditLogger;
    private readonly IUsbAccessPolicy _accessPolicy;
    private readonly string _principalId;
    private readonly IUsbDriver _driver;
    
    public RemoteUsbBlobStore(
        UsbDeviceInfo device,
        IUsbAuditLogger auditLogger,
        IUsbAccessPolicy accessPolicy,
        string principalId,
        IUsbDriver driver)
    {
        _device = device;
        _auditLogger = auditLogger;
        _accessPolicy = accessPolicy;
        _principalId = principalId;
        _driver = driver;
    }
    
    public async Task<Stream?> GetBlobStreamAsync(string id)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Validate access
            if (!await _accessPolicy.CanAccessDeviceAsync(
                _principalId, _device, UsbAccessType.Read))
            {
                await _auditLogger.LogAccessAsync(new UsbAuditEvent
                {
                    PrincipalId = _principalId,
                    DeviceId = _device.Id,
                    Operation = "GetBlobStream",
                    AccessType = UsbAccessType.Read,
                    Success = false,
                    ErrorMessage = "Access denied"
                });
                return null;
            }
            
            // Open device, seek to partition, read file
            // id = "/path/to/file.bin" on first partition
            var stream = await _driver.ReadFileAsync(_device.Id, id);
            
            sw.Stop();
            await _auditLogger.LogAccessAsync(new UsbAuditEvent
            {
                PrincipalId = _principalId,
                DeviceId = _device.Id,
                Operation = "GetBlobStream",
                AccessType = UsbAccessType.Read,
                Success = true,
                BytesTransferred = stream?.Length ?? 0,
                Duration = sw.Elapsed
            });
            
            return stream;
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _auditLogger.LogAccessAsync(new UsbAuditEvent
            {
                PrincipalId = _principalId,
                DeviceId = _device.Id,
                Operation = "GetBlobStream",
                AccessType = UsbAccessType.Read,
                Success = false,
                ErrorMessage = ex.Message,
                Duration = sw.Elapsed
            });
            throw;
        }
    }
    
    public async Task PutBlobAsync(string id, Stream content, string contentType)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Validate access
            if (_device.IsReadOnly || !await _accessPolicy.CanAccessDeviceAsync(
                _principalId, _device, UsbAccessType.Write))
            {
                await _auditLogger.LogAccessAsync(new UsbAuditEvent
                {
                    PrincipalId = _principalId,
                    DeviceId = _device.Id,
                    Operation = "PutBlob",
                    AccessType = UsbAccessType.Write,
                    Success = false,
                    ErrorMessage = "Write access denied or device read-only"
                });
                throw new InvalidOperationException("Device is read-only or write access denied");
            }
            
            // Write to device
            long bytesWritten = await _driver.WriteFileAsync(_device.Id, id, content);
            
            sw.Stop();
            await _auditLogger.LogAccessAsync(new UsbAuditEvent
            {
                PrincipalId = _principalId,
                DeviceId = _device.Id,
                Operation = "PutBlob",
                AccessType = UsbAccessType.Write,
                Success = true,
                BytesTransferred = bytesWritten,
                Duration = sw.Elapsed
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _auditLogger.LogAccessAsync(new UsbAuditEvent
            {
                PrincipalId = _principalId,
                DeviceId = _device.Id,
                Operation = "PutBlob",
                AccessType = UsbAccessType.Write,
                Success = false,
                ErrorMessage = ex.Message,
                Duration = sw.Elapsed
            });
            throw;
        }
    }
    
    public async Task DeleteBlobAsync(string id)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (_device.IsReadOnly || !await _accessPolicy.CanAccessDeviceAsync(
                _principalId, _device, UsbAccessType.Write))
            {
                throw new InvalidOperationException("Write access denied");
            }
            
            await _driver.DeleteFileAsync(_device.Id, id);
            
            sw.Stop();
            await _auditLogger.LogAccessAsync(new UsbAuditEvent
            {
                PrincipalId = _principalId,
                DeviceId = _device.Id,
                Operation = "DeleteBlob",
                AccessType = UsbAccessType.Write,
                Success = true,
                Duration = sw.Elapsed
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _auditLogger.LogAccessAsync(new UsbAuditEvent
            {
                PrincipalId = _principalId,
                DeviceId = _device.Id,
                Operation = "DeleteBlob",
                AccessType = UsbAccessType.Write,
                Success = false,
                ErrorMessage = ex.Message,
                Duration = sw.Elapsed
            });
            throw;
        }
    }
    
    public async Task<BlobMetadata?> GetBlobMetadataAsync(string id)
    {
        // TODO: fetch file size, modification time from device
        return await _driver.GetFileMetadataAsync(_device.Id, id);
    }
}
```

### 1.3 Platform Drivers

**File:** `Morpheo.Usb/Platform/IUsbDriver.cs`

```csharp
namespace Morpheo.Usb.Platform;

/// <summary>
/// Low-level USB operations: enumerate, read/write storage, send printer commands.
/// Implemented separately for Windows (WinUSB) and Linux (libusb).
/// </summary>
public interface IUsbDriver
{
    // Enumeration
    Task<IReadOnlyList<UsbDeviceInfo>> EnumerateDevicesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UsbDeviceInfo>> EnumerateByClassAsync(UsbDeviceClass @class, CancellationToken ct = default);
    
    // Storage I/O
    Task<Stream?> ReadFileAsync(string deviceId, string filePath, CancellationToken ct = default);
    Task<long> WriteFileAsync(string deviceId, string filePath, Stream content, CancellationToken ct = default);
    Task DeleteFileAsync(string deviceId, string filePath, CancellationToken ct = default);
    Task<BlobMetadata?> GetFileMetadataAsync(string deviceId, string filePath, CancellationToken ct = default);
    
    // Printing
    Task SendPrintJobAsync(string deviceId, byte[] data, CancellationToken ct = default);
    Task<PrinterStatus?> GetPrinterStatusAsync(string deviceId, CancellationToken ct = default);
}
```

**File:** `Morpheo.Usb/Platform/Windows/WinUsbEnumerator.cs`

```csharp
namespace Morpheo.Usb.Platform.Windows;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public class WinUsbEnumerator : IUsbDeviceEnumerator
{
    // P/Invoke declarations for SetupAPI
    // References: https://docs.microsoft.com/en-us/windows/win32/api/setupapi/
    
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr hwndParent,
        uint flags);
    
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SP_DEVINFO_DATA deviceInfoData);
    
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        StringBuilder deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);
    
    // Constants
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    
    // GUIDs for device classes
    private static readonly Guid GUID_DEVINTERFACE_USB_DEVICE =
        new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");
    
    private static readonly Guid GUID_DEVCLASS_CDROM =
        new Guid("4d36e965-e325-11ce-bfc1-08002be10318");
    
    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }
    
    public async Task<IReadOnlyList<UsbDeviceInfo>> EnumerateAsync(CancellationToken ct = default)
    {
        // TODO: Implement using SetupDi* APIs
        // 1. SetupDiGetClassDevs(GUID_DEVINTERFACE_USB_DEVICE, ...)
        // 2. Loop SetupDiEnumDeviceInfo
        // 3. Extract VendorId/ProductId from registry
        // 4. For storage devices: query partition info
        
        await Task.Delay(0, ct); // async placeholder
        return new List<UsbDeviceInfo>();
    }
    
    public Task<IReadOnlyList<UsbDeviceInfo>> EnumerateByClassAsync(
        UsbDeviceClass deviceClass, 
        CancellationToken ct = default)
    {
        // Filter by deviceClass
        return EnumerateAsync(ct);
    }
    
    public async Task<UsbDeviceInfo?> GetDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        var all = await EnumerateAsync(ct);
        return all.FirstOrDefault(d => d.Id == deviceId);
    }
}
```

**File:** `Morpheo.Usb/Platform/Linux/LibUsbEnumerator.cs`

```csharp
namespace Morpheo.Usb.Platform.Linux;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[SupportedOSPlatform("linux")]
public class LibUsbEnumerator : IUsbDeviceEnumerator
{
    // P/Invoke for libusb-1.0
    // Library: libusb-1.0.so.0
    
    [DllImport("libusb-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int libusb_init(out IntPtr ctx);
    
    [DllImport("libusb-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void libusb_exit(IntPtr ctx);
    
    [DllImport("libusb-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int libusb_get_device_list(IntPtr ctx, out IntPtr devs);
    
    [DllImport("libusb-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void libusb_free_device_list(IntPtr devs, int unref_devices);
    
    [DllImport("libusb-1.0.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int libusb_get_device_descriptor(IntPtr dev, ref USB_DEVICE_DESCRIPTOR desc);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct USB_DEVICE_DESCRIPTOR
    {
        public byte bLength;
        public byte bDescriptorType;
        public ushort bcdUSB;
        public byte bDeviceClass;
        public byte bDeviceSubClass;
        public byte bDeviceProtocol;
        public byte bMaxPacketSize0;
        public ushort idVendor;
        public ushort idProduct;
        public ushort bcdDevice;
        public byte iManufacturer;
        public byte iProduct;
        public byte iSerialNumber;
        public byte bNumConfigurations;
    }
    
    public async Task<IReadOnlyList<UsbDeviceInfo>> EnumerateAsync(CancellationToken ct = default)
    {
        // TODO: Implement using libusb_get_device_list
        // 1. libusb_init()
        // 2. libusb_get_device_list()
        // 3. Loop + libusb_get_device_descriptor()
        // 4. For storage: read /sys/block/sd*/size
        
        await Task.Delay(0, ct);
        return new List<UsbDeviceInfo>();
    }
    
    public Task<IReadOnlyList<UsbDeviceInfo>> EnumerateByClassAsync(
        UsbDeviceClass deviceClass, 
        CancellationToken ct = default)
    {
        return EnumerateAsync(ct);
    }
    
    public async Task<UsbDeviceInfo?> GetDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        var all = await EnumerateAsync(ct);
        return all.FirstOrDefault(d => d.Id == deviceId);
    }
}
```

### 1.4 Web Server Endpoints

**File:** Add to `Morpheo.Core/Server/MorpheoWebServer.cs`, after health checks:

```csharp
// USB Storage & Printing endpoints
_app.MapGet("/api/usb/devices", async (HttpContext context, 
    [FromServices] IUsbDeviceEnumerator enumerator,
    [FromServices] IUsbAccessPolicy accessPolicy) =>
{
    var principalId = context.User.FindFirst("sub")?.Value ?? "anonymous";
    var devices = await enumerator.EnumerateAsync(context.RequestAborted);
    
    // Filter by access policy
    var accessible = new List<object>();
    foreach (var device in devices)
    {
        var canAccess = await accessPolicy.CanAccessDeviceAsync(
            principalId, device, UsbAccessType.Read);
        if (canAccess)
        {
            accessible.Add(new
            {
                device.Id,
                device.VendorId,
                device.ProductId,
                device.Manufacturer,
                device.ProductName,
                device.DeviceClass,
                device.CapacityBytes,
                device.IsReadOnly,
                device.SerialNumber
            });
        }
    }
    
    return Results.Ok(accessible);
});

_app.MapGet("/api/usb/storage/{deviceId}/mount", async (string deviceId, HttpContext context,
    [FromServices] IUsbDeviceEnumerator enumerator,
    [FromServices] IUsbAccessPolicy accessPolicy) =>
{
    var principalId = context.User.FindFirst("sub")?.Value ?? "anonymous";
    var device = await enumerator.GetDeviceAsync(deviceId);
    
    if (device?.DeviceClass != UsbDeviceClass.Storage)
        return Results.NotFound("Device not found or not a storage device");
    
    if (!await accessPolicy.CanAccessDeviceAsync(principalId, device, UsbAccessType.Read))
        return Results.Forbid();
    
    // Return mount info
    return Results.Ok(new
    {
        mountUrl = $"/morpheo/usb/storage/{deviceId}",
        expiresIn = TimeSpan.FromHours(1).TotalSeconds,
        deviceInfo = device
    });
});

_app.MapGet("/morpheo/usb/storage/{deviceId}/{*path}", async (string deviceId, string? path, HttpContext context,
    [FromServices] IUsbDeviceEnumerator enumerator,
    [FromServices] IUsbStorageAdapter storageAdapter,
    [FromServices] IUsbAccessPolicy accessPolicy) =>
{
    var principalId = context.User.FindFirst("sub")?.Value ?? "anonymous";
    var device = await enumerator.GetDeviceAsync(deviceId);
    
    if (device?.DeviceClass != UsbDeviceClass.Storage)
        return Results.NotFound();
    
    if (!await accessPolicy.CanAccessDeviceAsync(principalId, device, UsbAccessType.Read))
        return Results.Forbid();
    
    // Create virtual store
    var store = await storageAdapter.CreateVirtualStorageAsync(device, null, accessPolicy);
    
    // Serve file or directory listing
    if (string.IsNullOrEmpty(path))
        path = "/";
    
    try
    {
        var stream = await store.GetBlobStreamAsync(path);
        if (stream == null)
            return Results.NotFound();
        
        return Results.File(stream, "application/octet-stream", Path.GetFileName(path));
    }
    catch
    {
        return Results.NotFound();
    }
});
```

---

## Phase 2: USB Printing (Weeks 4-5)

### 2.1 Printer-Specific Interfaces

**File:** `Morpheo.Usb/Printing/IUsbPrinterAdapter.cs`

```csharp
namespace Morpheo.Usb.Printing;

public interface IUsbPrinterAdapter
{
    /// <summary>Create a virtual printer from a USB device.</summary>
    Task<IPrintGateway> CreateVirtualPrinterAsync(
        UsbDeviceInfo device,
        IUsbAuditLogger auditLogger,
        IUsbAccessPolicy accessPolicy,
        string principalId,
        CancellationToken ct = default);
}
```

**File:** `Morpheo.Usb/Printing/RemoteUsbPrinter.cs`

```csharp
namespace Morpheo.Usb.Printing;

using Morpheo.Sdk;
using Morpheo.Usb.Audit;

/// <summary>
/// Implements IPrintGateway by sending print jobs to a USB printer device.
/// Supports RAW protocol (any printer model).
/// </summary>
public class RemoteUsbPrinter : IPrintGateway
{
    private readonly UsbDeviceInfo _device;
    private readonly IUsbAuditLogger _auditLogger;
    private readonly IUsbAccessPolicy _accessPolicy;
    private readonly string _principalId;
    private readonly IUsbDriver _driver;
    
    public RemoteUsbPrinter(
        UsbDeviceInfo device,
        IUsbAuditLogger auditLogger,
        IUsbAccessPolicy accessPolicy,
        string principalId,
        IUsbDriver driver)
    {
        _device = device;
        _auditLogger = auditLogger;
        _accessPolicy = accessPolicy;
        _principalId = principalId;
        _driver = driver;
    }
    
    public async Task PrintAsync(string printerName, byte[] data, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Validate access
            if (!await _accessPolicy.CanAccessDeviceAsync(
                _principalId, _device, UsbAccessType.Write, cancellationToken))
            {
                await _auditLogger.LogAccessAsync(new UsbAuditEvent
                {
                    PrincipalId = _principalId,
                    DeviceId = _device.Id,
                    Operation = "SendPrintJob",
                    AccessType = UsbAccessType.Write,
                    Success = false,
                    ErrorMessage = "Access denied",
                    BytesTransferred = data.Length
                });
                throw new UnauthorizedAccessException("Print access denied");
            }
            
            // Send to device
            await _driver.SendPrintJobAsync(_device.Id, data, cancellationToken);
            
            sw.Stop();
            await _auditLogger.LogAccessAsync(new UsbAuditEvent
            {
                PrincipalId = _principalId,
                DeviceId = _device.Id,
                Operation = "SendPrintJob",
                AccessType = UsbAccessType.Write,
                Success = true,
                BytesTransferred = data.Length,
                Duration = sw.Elapsed
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _auditLogger.LogAccessAsync(new UsbAuditEvent
            {
                PrincipalId = _principalId,
                DeviceId = _device.Id,
                Operation = "SendPrintJob",
                AccessType = UsbAccessType.Write,
                Success = false,
                ErrorMessage = ex.Message,
                BytesTransferred = data.Length,
                Duration = sw.Elapsed
            });
            throw;
        }
    }
    
    public async Task<PrinterStatus?> GetStatusAsync(CancellationToken cancellationToken)
    {
        return await _driver.GetPrinterStatusAsync(_device.Id, cancellationToken);
    }
}
```

### 2.2 Platform-Specific Printer Adapters

**File:** `Morpheo.Usb/Platform/Windows/WindowsPrinterAdapter.cs`

```csharp
namespace Morpheo.Usb.Platform.Windows;

using System.Runtime.Versioning;

[SupportedOSPlatform("windows")]
public class WindowsPrinterAdapter : IUsbPrinterAdapter
{
    private readonly IUsbDriver _driver;
    
    public WindowsPrinterAdapter(IUsbDriver driver)
    {
        _driver = driver;
    }
    
    public async Task<IPrintGateway> CreateVirtualPrinterAsync(
        UsbDeviceInfo device,
        IUsbAuditLogger auditLogger,
        IUsbAccessPolicy accessPolicy,
        string principalId,
        CancellationToken ct = default)
    {
        if (device.DeviceClass != UsbDeviceClass.Printer)
            throw new ArgumentException("Device is not a printer");
        
        return new RemoteUsbPrinter(device, auditLogger, accessPolicy, principalId, _driver);
    }
}

// TODO: Implement Windows printer enumeration
// - Use SetupDi* APIs to find GUID_DEVCLASS_PRINTER devices
// - Extract VendorId/ProductId, model name
// - Detect write endpoint for RAW printing
```

**File:** `Morpheo.Usb/Platform/Linux/LinuxPrinterAdapter.cs`

```csharp
namespace Morpheo.Usb.Platform.Linux;

using System.Runtime.Versioning;
using System.Diagnostics;

[SupportedOSPlatform("linux")]
public class LinuxPrinterAdapter : IUsbPrinterAdapter
{
    private readonly IUsbDriver _driver;
    
    public LinuxPrinterAdapter(IUsbDriver driver)
    {
        _driver = driver;
    }
    
    public async Task<IPrintGateway> CreateVirtualPrinterAsync(
        UsbDeviceInfo device,
        IUsbAuditLogger auditLogger,
        IUsbAccessPolicy accessPolicy,
        string principalId,
        CancellationToken ct = default)
    {
        if (device.DeviceClass != UsbDeviceClass.Printer)
            throw new ArgumentException("Device is not a printer");
        
        return new RemoteUsbPrinter(device, auditLogger, accessPolicy, principalId, _driver);
    }
}

// TODO: Implement Linux printer enumeration
// - Use CUPS API (cupsGetDests) or lpstat
// - Map CUPS printers to USB devices
// - Use lpadmin to create virtual printer if needed
```

### 2.3 Printer Endpoints

**File:** Add to `MorpheoWebServer.cs`:

```csharp
_app.MapGet("/api/usb/printers", async (HttpContext context,
    [FromServices] IUsbDeviceEnumerator enumerator,
    [FromServices] IUsbAccessPolicy accessPolicy) =>
{
    var principalId = context.User.FindFirst("sub")?.Value ?? "anonymous";
    var printers = await enumerator.EnumerateByClassAsync(UsbDeviceClass.Printer);
    
    var accessible = new List<object>();
    foreach (var printer in printers)
    {
        if (await accessPolicy.CanAccessDeviceAsync(principalId, printer, UsbAccessType.Write))
        {
            accessible.Add(new
            {
                printer.Id,
                printer.Manufacturer,
                printer.ProductName,
                printer.VendorId,
                printer.ProductId,
                printer.SerialNumber
            });
        }
    }
    
    return Results.Ok(accessible);
});

_app.MapPost("/api/usb/print/{deviceId}", async (string deviceId, HttpContext context,
    [FromBody] PrintJobRequest request,
    [FromServices] IUsbDeviceEnumerator enumerator,
    [FromServices] IUsbPrinterAdapter printerAdapter,
    [FromServices] IUsbAccessPolicy accessPolicy,
    [FromServices] IUsbAuditLogger auditLogger) =>
{
    var principalId = context.User.FindFirst("sub")?.Value ?? "anonymous";
    var device = await enumerator.GetDeviceAsync(deviceId);
    
    if (device?.DeviceClass != UsbDeviceClass.Printer)
        return Results.NotFound("Device not found or not a printer");
    
    if (!await accessPolicy.CanAccessDeviceAsync(principalId, device, UsbAccessType.Write))
        return Results.Forbid();
    
    try
    {
        var gateway = await printerAdapter.CreateVirtualPrinterAsync(
            device, auditLogger, accessPolicy, principalId);
        
        await gateway.PrintAsync(device.ProductName ?? device.Id, request.Data, context.RequestAborted);
        
        return Results.Accepted();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

_app.MapGet("/api/usb/print/{deviceId}/status", async (string deviceId, HttpContext context,
    [FromServices] IUsbDeviceEnumerator enumerator,
    [FromServices] IUsbAccessPolicy accessPolicy,
    [FromServices] IUsbDriver driver) =>
{
    var principalId = context.User.FindFirst("sub")?.Value ?? "anonymous";
    var device = await enumerator.GetDeviceAsync(deviceId);
    
    if (device?.DeviceClass != UsbDeviceClass.Printer)
        return Results.NotFound();
    
    if (!await accessPolicy.CanAccessDeviceAsync(principalId, device, UsbAccessType.Read))
        return Results.Forbid();
    
    var status = await driver.GetPrinterStatusAsync(deviceId);
    return Results.Ok(status);
});

public class PrintJobRequest
{
    public required byte[] Data { get; set; }
    public string? ContentType { get; set; } = "application/pdf"; // or text/plain, etc.
}
```

---

## Phase 3: Integration & Security (Weeks 6-8)

### 3.1 Default Access Policy

**File:** `Morpheo.Usb/Core/DefaultUsbAccessPolicy.cs`

```csharp
namespace Morpheo.Usb;

/// <summary>
/// Default implementation: authenticate via JWT, authorize via device allowlist.
/// </summary>
public class DefaultUsbAccessPolicy : IUsbAccessPolicy
{
    private readonly Dictionary<string, HashSet<string>> _principalDeviceAllowlist;
    private readonly ILogger<DefaultUsbAccessPolicy> _logger;
    
    public DefaultUsbAccessPolicy(
        UsbSharingOptions options,
        ILogger<DefaultUsbAccessPolicy> logger)
    {
        _logger = logger;
        _principalDeviceAllowlist = options.PrincipalDeviceAllowlist ?? new();
    }
    
    public Task<bool> CanAccessDeviceAsync(
        string principalId,
        UsbDeviceInfo device,
        UsbAccessType accessType,
        CancellationToken ct = default)
    {
        // Admin can access everything
        if (principalId == "admin")
            return Task.FromResult(true);
        
        // Check allowlist
        if (_principalDeviceAllowlist.TryGetValue(principalId, out var allowedIds))
        {
            return Task.FromResult(allowedIds.Contains(device.Id));
        }
        
        _logger.LogWarning("Access denied: {PrincipalId} -> {DeviceId}", principalId, device.Id);
        return Task.FromResult(false);
    }
}
```

### 3.2 Service Extension

**File:** `Morpheo.Core/Extensions/MorpheoUsbExtensions.cs` (NEW)

```csharp
namespace Morpheo.Core.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Morpheo.Usb;
using Morpheo.Usb.Audit;
using Morpheo.Usb.Platform;
using Morpheo.Usb.Storage;
using Morpheo.Usb.Printing;

public static class MorpheoUsbExtensions
{
    /// <summary>
    /// Add USB Storage & Printing device virtualization to Morpheo.
    /// </summary>
    public static IMorpheoBuilder AddUsbSharing(
        this IMorpheoBuilder builder,
        Action<UsbSharingOptions>? configure = null)
    {
        var services = builder.Services;
        var options = new UsbSharingOptions();
        configure?.Invoke(options);
        
        services.AddSingleton(options);
        
        // Platform-specific driver
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IUsbDriver, WinUsbDriver>();
            services.AddSingleton<IUsbDeviceEnumerator, WinUsbEnumerator>();
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IUsbDriver, LibUsbDriver>();
            services.AddSingleton<IUsbDeviceEnumerator, LibUsbEnumerator>();
        }
        else
        {
            // macOS/other: placeholder
            throw new NotSupportedException("USB sharing not yet supported on this platform");
        }
        
        // Adapters & logging
        services.AddSingleton<IUsbStorageAdapter, UsbStorageAdapter>();
        services.AddSingleton<IUsbPrinterAdapter, UsbPrinterAdapter>();
        services.AddSingleton<IUsbAuditLogger, UsbAuditLogger>();
        
        // Access policy
        if (options.AccessPolicy == null)
            services.AddSingleton<IUsbAccessPolicy, DefaultUsbAccessPolicy>();
        else
            services.AddSingleton(options.AccessPolicy);
        
        return builder;
    }
}
```

**File:** `Morpheo.Usb/UsbSharingOptions.cs`

```csharp
namespace Morpheo.Usb;

/// <summary>Configuration for USB device sharing.</summary>
public class UsbSharingOptions
{
    /// <summary>Enable USB storage device access.</summary>
    public bool EnableStorage { get; set; } = true;
    
    /// <summary>Enable USB printer access.</summary>
    public bool EnablePrinting { get; set; } = true;
    
    /// <summary>Allowed USB vendor IDs (null = allow all). E.g., 0x0951 for Kingston.</summary>
    public IReadOnlySet<ushort>? AllowedVendorIds { get; set; }
    
    /// <summary>Blocked USB vendor IDs (blacklist).</summary>
    public IReadOnlySet<ushort>? BlockedVendorIds { get; set; }
    
    /// <summary>Principal (user/service) -> allowed device IDs mapping.</summary>
    public Dictionary<string, HashSet<string>>? PrincipalDeviceAllowlist { get; set; }
    
    /// <summary>Custom access policy implementation.</summary>
    public IUsbAccessPolicy? AccessPolicy { get; set; }
    
    /// <summary>Max concurrent USB connections per principal.</summary>
    public int MaxConcurrentConnectionsPerPrincipal { get; set; } = 10;
}
```

### 3.3 Audit Logger Implementation

**File:** `Morpheo.Usb/Audit/UsbAuditLogger.cs`

```csharp
namespace Morpheo.Usb.Audit;

using Microsoft.Extensions.Logging;

/// <summary>
/// Stores USB access events in memory (for demo) or database (for production).
/// </summary>
public class UsbAuditLogger : IUsbAuditLogger
{
    private readonly List<UsbAuditEvent> _events = new();
    private readonly ILogger<UsbAuditLogger> _logger;
    private readonly object _lock = new();
    
    public UsbAuditLogger(ILogger<UsbAuditLogger> logger)
    {
        _logger = logger;
    }
    
    public async Task LogAccessAsync(UsbAuditEvent @event, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _events.Add(@event);
        }
        
        _logger.Log(
            @event.Success ? LogLevel.Information : LogLevel.Warning,
            "USB Access: {Operation} on {DeviceId} by {PrincipalId} - {@Event}",
            @event.Operation, @event.DeviceId, @event.PrincipalId, @event);
        
        // TODO: Persist to database
        await Task.CompletedTask;
    }
    
    public async Task<IReadOnlyList<UsbAuditEvent>> QueryAsync(
        string? principalId = null,
        string? deviceId = null,
        DateTimeOffset? since = null,
        int limit = 1000,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            var query = _events.AsEnumerable();
            
            if (!string.IsNullOrEmpty(principalId))
                query = query.Where(e => e.PrincipalId == principalId);
            
            if (!string.IsNullOrEmpty(deviceId))
                query = query.Where(e => e.DeviceId == deviceId);
            
            if (since.HasValue)
                query = query.Where(e => e.Timestamp >= since.Value.UtcDateTime);
            
            return query
                .OrderByDescending(e => e.Timestamp)
                .Take(limit)
                .ToList()
                .AsReadOnly();
        }
    }
}
```

### 3.4 Audit Endpoint

**File:** Add to `MorpheoWebServer.cs`:

```csharp
_app.MapGet("/api/usb/audit", async (HttpContext context,
    [FromQuery] string? principalId,
    [FromQuery] string? deviceId,
    [FromQuery] int limit,
    [FromServices] IUsbAuditLogger auditLogger) =>
{
    // Only allow admin to view full audit log
    var requester = context.User.FindFirst("sub")?.Value;
    if (requester != "admin" && requester != principalId)
        return Results.Forbid();
    
    var events = await auditLogger.QueryAsync(principalId, deviceId, limit: limit);
    return Results.Ok(events);
});
```

---

## Dependencies

### NuGet Packages

```xml
<!-- Morpheo.Usb.csproj -->
<ItemGroup>
    <ProjectReference Include="../Morpheo.Core/Morpheo.Core.csproj" />
    <ProjectReference Include="../Morpheo.Sdk/Morpheo.Sdk.csproj" />
    
    <!-- Windows P/Invoke -->
    <PackageReference Include="PInvoke.SetupApi" Version="0.7.*" Condition="'$(RuntimeIdentifier)' == 'win-x64'" />
    <PackageReference Include="PInvoke.Kernel32" Version="0.7.*" Condition="'$(RuntimeIdentifier)' == 'win-x64'" />
    <PackageReference Include="PInvoke.Ntdef" Version="0.7.*" Condition="'$(RuntimeIdentifier)' == 'win-x64'" />
    
    <!-- Linux libusb -->
    <PackageReference Include="LibUsbDotNet" Version="2.2.*" Condition="'$(RuntimeIdentifier)' == 'linux-x64'" />
    
    <!-- Logging -->
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
</ItemGroup>

<PropertyGroup>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>CS1591</NoWarn>
</PropertyGroup>
```

### System Dependencies

**Windows:**
- WinUSB driver (built-in on Windows 10+)
- SetupAPI (system library)

**Linux:**
- `libusb-1.0-0-dev` package
- `libcups2-dev` package (for printing)
- udev rules for USB device permissions

```bash
# Ubuntu/Debian
sudo apt-get install libusb-1.0-0-dev libcups2-dev
```

---

## Testing Strategy

### Unit Tests

**File:** `Morpheo.Tests/Usb/UsbAuditLoggerTests.cs`

```csharp
public class UsbAuditLoggerTests
{
    [Fact]
    public async Task LogAccessAsync_ShouldStoreEvent()
    {
        var logger = CreateLogger();
        var @event = new UsbAuditEvent
        {
            PrincipalId = "user1",
            DeviceId = "device1",
            Operation = "Read",
            AccessType = UsbAccessType.Read,
            Success = true
        };
        
        await logger.LogAccessAsync(@event);
        
        var events = await logger.QueryAsync(principalId: "user1");
        events.Should().ContainSingle();
    }
    
    [Fact]
    public async Task QueryAsync_ShouldFilterByPrincipal()
    {
        // TODO: test filtering
    }
}
```

### Integration Tests

**File:** `Morpheo.Tests/Usb/UsbEnumerationIntegrationTests.cs`

```csharp
[Trait("Category", "Integration")]
public class UsbEnumerationIntegrationTests
{
    [SkippableFact]
    public async Task EnumerateDevices_ShouldReturnConnectedDevices()
    {
        Skip.If(!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux(),
            "USB enumeration not supported on this platform");
        
        var enumerator = CreateEnumerator();
        var devices = await enumerator.EnumerateAsync();
        
        // May be empty if no USB devices connected
        devices.Should().NotBeNull();
    }
    
    [SkippableFact]
    public async Task EnumerateByClass_ShouldFilterStorageDevices()
    {
        Skip.If(!HasUsbStorageDevice(), "No USB storage device connected");
        
        var enumerator = CreateEnumerator();
        var storage = await enumerator.EnumerateByClassAsync(UsbDeviceClass.Storage);
        
        storage.Should().NotBeEmpty();
        storage.All(d => d.DeviceClass == UsbDeviceClass.Storage).Should().BeTrue();
    }
}
```

### End-to-End Tests

**File:** `Morpheo.Tests/Usb/UsbStorageE2eTests.cs`

```csharp
[Trait("Category", "E2E")]
public class UsbStorageE2eTests : IAsyncLifetime
{
    private MorpheoWebServer _server;
    private HttpClient _client;
    
    public async Task InitializeAsync()
    {
        // Setup real Morpheo server with USB sharing enabled
        var services = new ServiceCollection();
        services.AddMorpheo(m => m.AddUsbSharing());
        
        var provider = services.BuildServiceProvider();
        _server = new MorpheoWebServer(new(), provider);
        await _server.StartAsync(CancellationToken.None);
        
        _client = new HttpClient { BaseAddress = new Uri($"http://localhost:{_server.LocalPort}") };
    }
    
    public async Task DisposeAsync()
    {
        await _server.StopAsync(CancellationToken.None);
        _client.Dispose();
    }
    
    [SkippableFact]
    public async Task GetUsbDevices_ShouldReturnJson()
    {
        Skip.If(!HasUsbDevice(), "No USB device connected");
        
        var response = await _client.GetAsync("/api/usb/devices");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("VendorId");
    }
    
    [SkippableFact]
    public async Task MountUsbStorage_ShouldReturnMountUrl()
    {
        Skip.If(!HasUsbStorageDevice(), "No USB storage device connected");
        
        var devices = await _client.GetAsync("/api/usb/devices");
        var storage = (await devices.Content.ReadAsAsync<List<dynamic>>())
            .FirstOrDefault(d => d.DeviceClass == "Storage");
        
        Skip.If(storage == null, "No storage device found");
        
        var mount = await _client.GetAsync($"/api/usb/storage/{storage.Id}/mount");
        mount.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var json = await mount.Content.ReadAsAsync<dynamic>();
        json.mountUrl.Should().NotBeNull();
    }
}
```

---

## Security & Audit

### Authentication

All `/api/usb/*` endpoints require a valid JWT token in the `Authorization: Bearer <token>` header.

**Principal ID extracted from:**
```csharp
context.User.FindFirst("sub")?.Value // JWT "sub" claim
```

### Authorization

1. **Device Access Control** via `IUsbAccessPolicy`
   - Admin (principal_id = "admin") → full access
   - Others → check allowlist from `UsbSharingOptions.PrincipalDeviceAllowlist`
   - Example:
     ```json
     {
       "PrincipalDeviceAllowlist": {
         "user1": ["usb-device-123", "usb-device-456"],
         "user2": ["usb-device-789"]
       }
     }
     ```

2. **Operation-Level Access**
   - `UsbAccessType.Read` for enumerate, mount, list files
   - `UsbAccessType.Write` for upload, delete, print
   - Printers: always require Write access

### Audit Logging

Every USB operation is logged with:
- **Who** (PrincipalId from JWT)
- **What** (Operation: Read, Write, Mount, Print)
- **When** (Timestamp)
- **Where** (DeviceId)
- **Success/Failure** (Success flag + ErrorMessage)
- **How Much** (BytesTransferred for data operations)
- **How Long** (Duration in milliseconds)

**Example audit event:**
```json
{
  "id": "audit-001",
  "principalId": "user@example.com",
  "deviceId": "usb-0x0951:0x1234",
  "operation": "ReadFile",
  "accessType": "Read",
  "success": true,
  "bytesTransferred": 52428800,
  "duration": "00:00:02.345",
  "timestamp": "2026-06-08T10:30:45Z",
  "clientIp": "192.168.1.100"
}
```

**Query audit log:**
```bash
curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5000/api/usb/audit?principalId=user1&since=2026-06-01"
```

---

## Implementation Checklist

### Phase 1: USB Storage

- [ ] Create Morpheo.Usb project
- [ ] Implement `IUsbDeviceEnumerator` interfaces
  - [ ] Windows: `WinUsbEnumerator` (SetupDi* APIs)
  - [ ] Linux: `LibUsbEnumerator` (libusb)
- [ ] Implement `IUsbDriver` for platform-specific I/O
  - [ ] Windows: `WinUsbDriver` (IOCTL_USB_* control codes)
  - [ ] Linux: `LibUsbDriver` (libusb bulk transfer)
- [ ] Implement `RemoteUsbBlobStore` (IMorpheoBlobStore)
- [ ] Add `/api/usb/devices` endpoint
- [ ] Add `/api/usb/storage/{id}/mount` endpoint
- [ ] Add `/morpheo/usb/storage/{id}/*` virtual filesystem endpoint
- [ ] Unit tests for enumeration
- [ ] Integration tests with real USB device (optional)

### Phase 2: USB Printing

- [ ] Implement `IUsbPrinterAdapter`
  - [ ] Windows: `WindowsPrinterAdapter`
  - [ ] Linux: `LinuxPrinterAdapter`
- [ ] Implement `RemoteUsbPrinter` (IPrintGateway)
- [ ] Extend `IUsbDriver` with print methods
  - [ ] `SendPrintJobAsync(deviceId, data)`
  - [ ] `GetPrinterStatusAsync(deviceId)`
- [ ] Add `/api/usb/printers` endpoint
- [ ] Add `/api/usb/print/{id}` POST endpoint
- [ ] Add `/api/usb/print/{id}/status` endpoint
- [ ] Unit tests for printing
- [ ] Integration tests with real printer (optional)

### Phase 3: Integration & Security

- [ ] Implement `IUsbAccessPolicy` (DefaultUsbAccessPolicy)
- [ ] Implement `IUsbAuditLogger` (UsbAuditLogger)
- [ ] Create `MorpheoUsbExtensions.AddUsbSharing()`
- [ ] Integrate USB endpoints into `MorpheoWebServer`
- [ ] Add auth middleware check for `/api/usb/*`
- [ ] Add `/api/usb/audit` endpoint
- [ ] Update `MorpheoServiceExtensions.cs` to register USB services
- [ ] End-to-end tests with real server
- [ ] Security review (auth, SQL injection in paths, etc.)
- [ ] Documentation
- [ ] Performance testing with large files

---

## Timeline

| Week | Phase | Tasks | Status |
|------|-------|-------|--------|
| 1 | 1a | Project setup, core interfaces, Windows enumeration | TBD |
| 2 | 1b | Linux enumeration, storage adapter, blob store impl | TBD |
| 3 | 1c | Endpoints, unit tests, integration tests | TBD |
| 4 | 2a | Printer adapter, platform drivers | TBD |
| 5 | 2b | Print endpoints, tests | TBD |
| 6 | 3a | Access policy, audit logging | TBD |
| 7 | 3b | Security hardening, documentation | TBD |
| 8 | 3c | Performance tuning, final testing | TBD |

---

## Notes for Implementation Team

1. **Start with Windows** if developing locally on Windows, then add Linux support.
2. **USB device permissions** are critical:
   - Windows: needs admin or WinUSB driver installation
   - Linux: needs udev rules or sudo (see `/etc/udev/rules.d/99-usb.rules`)
3. **RAW printing** assumes printer supports RAW protocol (most do). For PPD-based printers, may need CUPS integration.
4. **Large file transfers** can timeout — consider streaming with chunked encoding.
5. **Error handling** must be robust — USB can disconnect unexpectedly.
6. **Logging verbosity** should be configurable to avoid audit log spam.

---

**Document prepared for:** AI implementation team  
**Status:** Ready for development  
**Questions:** Contact system owner
