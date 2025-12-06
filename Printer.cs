using System;
using System.Collections.Generic;
using System.Text;

using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.ApplicationModel.Background;

namespace bluetoothTest
{
    internal class Printer
    {
        public ulong? TargetDeviceID { get; private set; }

        public bool FindPrinter()
        { 
            TargetDeviceID = null;
            var founded = new HashSet<ulong>();

            var tcs = new TaskCompletionSource();
            var watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            watcher.Received += async (sender, args) =>
            {
                var bluetoothAddress = args.BluetoothAddress;

                if (founded.Contains(bluetoothAddress))
                    return;
                founded.Add(bluetoothAddress);

                var deviceName = "UNKNOWN";

                try
                {
                    using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                    if (device != null)
                        deviceName = device.Name;
                }
                catch { }


                if (!deviceName.StartsWith("X6h"))
                    return;

                watcher.Stop();
                TargetDeviceID = bluetoothAddress;
            };

            watcher.Stopped += (sender, args) =>
            {
                tcs.TrySetResult();
            };

            watcher.Start();
            Task.WhenAny(tcs.Task).Wait();

            return TargetDeviceID.HasValue;
        }

        public async Task<List<GattDeviceService>> GetServicesAsync()
        {
            var services = new List<GattDeviceService>();

            if (!TargetDeviceID.HasValue)
                return services;

            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(TargetDeviceID.Value);
            if (device == null)
                return services;

            var result = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            if (result.Status == GattCommunicationStatus.Success)
            {
                foreach (var service in result.Services)
                {
                    services.Add(service);
                }
            }

            return services;
        }
    }
}
