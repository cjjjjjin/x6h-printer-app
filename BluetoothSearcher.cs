using System;
using System.Collections.Generic;
using System.Text;
using Windows.Devices.Bluetooth.Advertisement;
using System.Linq;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using System.Threading.Tasks;

namespace bluetoothTest
{
    internal class BluetoothSearcher
    {
        private BluetoothLEAdvertisementWatcher? watcher;
        private Dictionary<ulong, BleDeviceInfo> discoveredDevices = new Dictionary<ulong, BleDeviceInfo>();

        public class BleDeviceInfo
        {
            public ulong BluetoothAddress { get; set; }
            public string? LocalName { get; set; }
            public short RssiValue { get; set; }
            public DateTimeOffset LastSeen { get; set; }
        }

        public class BleServiceInfo
        {
            public Guid Uuid { get; set; }
            public string Name { get; set; } = string.Empty;
            public List<BleCharacteristicInfo> Characteristics { get; set; } = new List<BleCharacteristicInfo>();
        }

        public class BleCharacteristicInfo
        {
            public Guid Uuid { get; set; }
            public string Name { get; set; } = string.Empty;
            public GattCharacteristicProperties Properties { get; set; }
            public string PropertiesDescription { get; set; } = string.Empty;
        }

        public event EventHandler<BleDeviceInfo>? DeviceDiscovered;

        public void StartScanning()
        {
            if (watcher != null)
            {
                Console.WriteLine("스캔이 이미 진행 중입니다.");
                return;
            }

            watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active
            };

            watcher.Received += OnAdvertisementReceived;
            watcher.Stopped += OnWatcherStopped;

            watcher.Start();
            Console.WriteLine("BLE 장비 스캔을 시작합니다...");
        }

        public void StopScanning()
        {
            if (watcher != null)
            {
                watcher.Stop();
                watcher.Received -= OnAdvertisementReceived;
                watcher.Stopped -= OnWatcherStopped;
                watcher = null;
                Console.WriteLine("BLE 장비 스캔을 중지했습니다.");
            }
        }

        public List<BleDeviceInfo> GetDiscoveredDevices()
        {
            return discoveredDevices.Values.OrderByDescending(d => d.RssiValue).ToList();
        }

        public async Task<List<BleServiceInfo>> GetDeviceServicesAsync(ulong bluetoothAddress)
        {
            var services = new List<BleServiceInfo>();

            try
            {
                Console.WriteLine($"\n장비 연결 중: {FormatBluetoothAddress(bluetoothAddress)}");
                
                var device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
                
                if (device == null)
                {
                    Console.WriteLine("장비에 연결할 수 없습니다.");
                    return services;
                }

                Console.WriteLine($"장비 연결 성공: {device.Name ?? "알 수 없음"}");
                Console.WriteLine($"연결 상태: {device.ConnectionStatus}");

                var gattResult = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);

                if (gattResult.Status == GattCommunicationStatus.Success)
                {
                    Console.WriteLine($"\n총 {gattResult.Services.Count}개의 서비스 발견:");

                    foreach (var service in gattResult.Services)
                    {
                        var serviceInfo = new BleServiceInfo
                        {
                            Uuid = service.Uuid,
                            Name = GetServiceName(service.Uuid)
                        };

                        Console.WriteLine($"\n서비스: {serviceInfo.Name}");
                        Console.WriteLine($"  UUID: {service.Uuid}");

                        // 특성(Characteristic) 조회
                        var characteristicsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                        
                        if (characteristicsResult.Status == GattCommunicationStatus.Success)
                        {
                            Console.WriteLine($"  특성 개수: {characteristicsResult.Characteristics.Count}");

                            foreach (var characteristic in characteristicsResult.Characteristics)
                            {
                                var charInfo = new BleCharacteristicInfo
                                {
                                    Uuid = characteristic.Uuid,
                                    Name = GetCharacteristicName(characteristic.Uuid),
                                    Properties = characteristic.CharacteristicProperties,
                                    PropertiesDescription = GetPropertiesDescription(characteristic.CharacteristicProperties)
                                };

                                serviceInfo.Characteristics.Add(charInfo);

                                Console.WriteLine($"    - {charInfo.Name}");
                                Console.WriteLine($"      UUID: {characteristic.Uuid}");
                                Console.WriteLine($"      속성: {charInfo.PropertiesDescription}");
                            }
                        }

                        services.Add(serviceInfo);
                        service.Dispose();
                    }
                }
                else
                {
                    Console.WriteLine($"서비스 조회 실패: {gattResult.Status}");
                }

                device.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"서비스 조회 중 오류 발생: {ex.Message}");
            }

            return services;
        }

        public async Task<List<BleServiceInfo>> GetDeviceServicesByNameAsync(string deviceName)
        {
            var device = discoveredDevices.Values.FirstOrDefault(d => 
                d.LocalName?.Equals(deviceName, StringComparison.OrdinalIgnoreCase) == true);

            if (device != null)
            {
                return await GetDeviceServicesAsync(device.BluetoothAddress);
            }

            Console.WriteLine($"장비를 찾을 수 없습니다: {deviceName}");
            return new List<BleServiceInfo>();
        }

        private string GetPropertiesDescription(GattCharacteristicProperties properties)
        {
            var props = new List<string>();

            if (properties.HasFlag(GattCharacteristicProperties.Read))
                props.Add("읽기");
            if (properties.HasFlag(GattCharacteristicProperties.Write))
                props.Add("쓰기");
            if (properties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
                props.Add("응답없이 쓰기");
            if (properties.HasFlag(GattCharacteristicProperties.Notify))
                props.Add("알림");
            if (properties.HasFlag(GattCharacteristicProperties.Indicate))
                props.Add("표시");
            if (properties.HasFlag(GattCharacteristicProperties.Broadcast))
                props.Add("브로드캐스트");

            return props.Count > 0 ? string.Join(", ", props) : "없음";
        }

        private string GetServiceName(Guid uuid)
        {
            // 표준 GATT 서비스 이름
            var knownServices = new Dictionary<Guid, string>
            {
                { new Guid("00001800-0000-1000-8000-00805f9b34fb"), "Generic Access" },
                { new Guid("00001801-0000-1000-8000-00805f9b34fb"), "Generic Attribute" },
                { new Guid("0000180a-0000-1000-8000-00805f9b34fb"), "Device Information" },
                { new Guid("0000180f-0000-1000-8000-00805f9b34fb"), "Battery Service" },
                { new Guid("00001812-0000-1000-8000-00805f9b34fb"), "Human Interface Device" },
                { new Guid("0000180d-0000-1000-8000-00805f9b34fb"), "Heart Rate" },
                { new Guid("00001816-0000-1000-8000-00805f9b34fb"), "Cycling Speed and Cadence" },
                { new Guid("00001818-0000-1000-8000-00805f9b34fb"), "Cycling Power" },
            };

            return knownServices.TryGetValue(uuid, out var name) ? name : "사용자 정의 서비스";
        }

        private string GetCharacteristicName(Guid uuid)
        {
            // 표준 GATT 특성 이름
            var knownCharacteristics = new Dictionary<Guid, string>
            {
                { new Guid("00002a00-0000-1000-8000-00805f9b34fb"), "Device Name" },
                { new Guid("00002a01-0000-1000-8000-00805f9b34fb"), "Appearance" },
                { new Guid("00002a19-0000-1000-8000-00805f9b34fb"), "Battery Level" },
                { new Guid("00002a29-0000-1000-8000-00805f9b34fb"), "Manufacturer Name" },
                { new Guid("00002a24-0000-1000-8000-00805f9b34fb"), "Model Number" },
                { new Guid("00002a25-0000-1000-8000-00805f9b34fb"), "Serial Number" },
                { new Guid("00002a27-0000-1000-8000-00805f9b34fb"), "Hardware Revision" },
                { new Guid("00002a26-0000-1000-8000-00805f9b34fb"), "Firmware Revision" },
                { new Guid("00002a28-0000-1000-8000-00805f9b34fb"), "Software Revision" },
            };

            return knownCharacteristics.TryGetValue(uuid, out var name) ? name : "사용자 정의 특성";
        }

        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var deviceInfo = new BleDeviceInfo
            {
                BluetoothAddress = args.BluetoothAddress,
                RssiValue = args.RawSignalStrengthInDBm,
                LastSeen = args.Timestamp
            };

            // 장비 이름 가져오기
            var localName = args.Advertisement.LocalName;
            if (!string.IsNullOrEmpty(localName))
            {
                deviceInfo.LocalName = localName;
            }

            // 장비 정보 업데이트 또는 추가
            bool isNewDevice = !discoveredDevices.ContainsKey(args.BluetoothAddress);
            discoveredDevices[args.BluetoothAddress] = deviceInfo;

            if (isNewDevice)
            {
                Console.WriteLine($"새 BLE 장비 발견: {FormatBluetoothAddress(args.BluetoothAddress)} " +
                                  $"이름: {deviceInfo.LocalName ?? "알 수 없음"} " +
                                  $"신호 강도: {args.RawSignalStrengthInDBm} dBm");
                
                DeviceDiscovered?.Invoke(this, deviceInfo);
            }
        }

        private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            Console.WriteLine($"스캔 중지됨. 상태: {args.Error}!");
        }

        private string FormatBluetoothAddress(ulong address)
        {
            byte[] bytes = BitConverter.GetBytes(address);
            Array.Reverse(bytes);
            return string.Join(":", bytes.Skip(2).Select(b => b.ToString("X2")));
        }

        public void ClearDiscoveredDevices()
        {
            discoveredDevices.Clear();
            Console.WriteLine("발견된 장비 목록을 초기화했습니다.");
        }
    }
}
