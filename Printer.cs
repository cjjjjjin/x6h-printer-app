using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using System.Text;
using Windows.ApplicationModel.Background;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using static bluetoothTest.Printer;

namespace bluetoothTest
{

    public class BleDevice
    {

        /// <summary>
        /// Bluetooth LE 장치를 검색합니다.
        /// </summary>
        /// <param name="timeoutMilliseconds">검색 타임아웃 (밀리초)</param>
        /// <returns></returns>
        protected ulong FindDevice(string deviceName, int timeoutMilliseconds = 10000)
        {
            // Initialize watcher
            ulong? deviceId = null;
            var founded = new HashSet<ulong>();
            var tcs = new TaskCompletionSource();

            var watcher = new BluetoothLEAdvertisementWatcher()
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

                if (!deviceName.StartsWith(deviceName))
                    return;

                watcher.Stop();
                deviceId = bluetoothAddress;
            };
            watcher.Stopped += (sender, args) =>
            {
                tcs.TrySetResult();
            };


            // Start watching
            watcher.Start();
            var completedTask = Task.WhenAny(tcs.Task, Task.Delay(timeoutMilliseconds)).Result;
            if (completedTask != tcs.Task)
            {
                watcher.Stop();
                throw new TimeoutException($"프린터 장치를 {timeoutMilliseconds}ms 이내에 찾지 못했습니다.");
            }


            // Return the found device ID
            if (null == deviceId)
                throw new Exception("프린터 장치를 찾지 못했습니다.");
            return deviceId.Value;
        }

        protected async Task<List<GattDeviceService>> GetServices(ulong deviceId)
        {
            // Get Bluetooth LE device
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(deviceId);
            if (device == null)
                throw new Exception("DeviceID를 이용하여 Bluetooth 장비를 찾지 못했습니다.");


            // Get GATT services
            var result = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            if (result.Status != GattCommunicationStatus.Success)
                throw new Exception($"Gatt 서비스 정보를 가져오지 못했습니다. {result.Status}");

            return result.Services.ToList();
        }


        protected async Task Write(GattCharacteristic c, byte[] data)
        {
            // Check printer
            if (null == c)
                throw new Exception("Writer 서비스가 준비되지 않았습니다.");

            if (!c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) &&
                !c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse))
                throw new Exception("Writer 서비스가 Write 속성을 지원하지 않습니다.");


            // Send data
            var writer = new Windows.Storage.Streams.DataWriter();
            writer.WriteBytes(data);
            var buffer = writer.DetachBuffer();

            var result = await c.WriteValueAsync(buffer);
            if (result != GattCommunicationStatus.Success)
                throw new Exception($"데이터 전송 실패: {result}");
        }


        public async Task<byte[]> Read(GattCharacteristic c, int timeout)
        {
            // Check printer
            if (null == c)
                throw new Exception("Reader 서비스가 준비되지 않았습니다.");

            if (!c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
                throw new Exception("Reader 서비스가 Read 속성을 지원하지 않습니다.");


            // Read from characteristic
            var result = await c.ReadValueAsync(BluetoothCacheMode.Uncached);
            if (result.Status != GattCommunicationStatus.Success)
                throw new Exception($"데이터 읽기 실패: {result.Status}");

            var reader = Windows.Storage.Streams.DataReader.FromBuffer(result.Value);
            var data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);

            return data;
        }

        public async Task<byte[]> ReadNotify(GattCharacteristic c, int timeoutMilliseconds = 5000)
        {
            // Check printer
            if (null == c)
                throw new Exception("Reader 서비스가 준비되지 않았습니다.");
            if (!c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify) &&
                !c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
                throw new Exception("Reader 서비스가 Notify 또는 Indicate 속성을 지원하지 않습니다.");


            // ValueChanged 이벤트 핸들러 등록
            var tcs = new TaskCompletionSource<byte[]>();
            TypedEventHandler<GattCharacteristic, GattValueChangedEventArgs> handler = null;
            handler = (sender, args) =>
            {
                var reader = Windows.Storage.Streams.DataReader.FromBuffer(args.CharacteristicValue);
                var data = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(data);

                // 이벤트 핸들러 해제
                c.ValueChanged -= handler;
                tcs.TrySetResult(data);
            };
            c.ValueChanged += handler;


            // CCCD 설정
            var status = await c.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (status != GattCommunicationStatus.Success)
            {
                c.ValueChanged -= handler;
                throw new Exception($"Notify 구독 설정 실패: {status}");
            }


            // 타임아웃 처리
            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMilliseconds));
            if (completedTask != tcs.Task)
            {
                c.ValueChanged -= handler;
                throw new TimeoutException($"Notify 데이터를 {timeoutMilliseconds}ms 이내에 받지 못했습니다.");
            }

            return await tcs.Task;
        }

    }


    public class Printer : BleDevice
    {
        protected PrinterGattService? _service = null;


        /// <summary>
        /// Prepare the printer
        /// </summary>
        /// <exception cref="Exception"></exception>
        public async Task PrepareAsync()
        {
            // Get BLE device
            var deviceId = FindDevice("X6h", 30000);


            // Get GATT services
            var gattDevices = await GetServices(deviceId);
            var service = new PrinterGattService();
            foreach (var s in gattDevices)
            {
                var characteristicsResult = await s.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                if (characteristicsResult.Status != GattCommunicationStatus.Success)
                    continue;
                foreach (var characteristic in characteristicsResult.Characteristics)
                {
                    if (characteristic.Uuid == Guid.Parse("0000ae01-0000-1000-8000-00805f9b34fb"))
                        service.Writer = characteristic;
                    else if (characteristic.Uuid == Guid.Parse("0000ae02-0000-1000-8000-00805f9b34fb"))
                        service.Reader = characteristic;
                }
            }

            if (null == service.Writer || null == service.Reader)
                throw new Exception("프린터의 GATT 서비스를 찾지 못했습니다.");
            _service = service;
        }
        public void Prepare() { PrepareAsync().Wait(); }


        /// <summary>
        /// 용지를 Pixels 단위로 공급
        /// </summary>
        public void FeedPaper(ushort pixels)
        { 
            if (null == _service)
                throw new Exception("프린터가 준비되지 않았습니다.");

            var payload = BitConverter.GetBytes(pixels);
            Write(Command.FeedPaper, payload).Wait();
        }

        public void WriteText(string text)
        {

        }



        public void Draw4BitGrayImage(byte[] imageData)
        {
            // Compress image data
            var compressed = Compress(imageData);


            // Make packet
            var payload = new byte[4 + compressed.Length];
            Array.Copy(BitConverter.GetBytes((ushort)imageData.Length), 0, payload, 0, 2); // Uncompressed length
            Array.Copy(BitConverter.GetBytes((ushort)compressed.Length), 0, payload, 2, 2); // Compressed length
            Array.Copy(compressed, 0, payload, 4, compressed.Length); // Compressed data

            // Send command
            Write(Command.GrayCompressedScanlineData, payload).Wait();
        }


        public async Task GetDeviceStatusAsync()
        {
            if (null == _service)
                throw new Exception("프린터가 준비되지 않았습니다.");

            // Prepare Read
            var readTask = ReadNotify(_service.Reader, 5000);

            // Send Command
            var payload = new byte[0];
            Write(Command.DeviceStatus, payload).Wait();

            // Get Data
            var data = await readTask;

            int a = 0;
        }



        /// <summary>
        /// Calculate CRC8
        /// </summary>
        private byte CalculateCrc8(byte[] data)
        {
            byte crc = 0x00;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x80) != 0)
                        crc = (byte)((crc << 1) ^ 0x07);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }


        /// <summary>
        /// Send message to Printer
        /// </summary>
        protected async Task Write(Command command, byte[] payload, byte? crc = null)
        {
            // Check printer
            if (null == _service)
                throw new Exception("프린터가 준비되지 않았습니다.");
            if (null == _service.Writer)
                throw new Exception("Writer 서비스가 준비되지 않았습니다.");


            // Create data
            if (crc == null)
                crc = CalculateCrc8(payload);
            var payloadLength = BitConverter.GetBytes((ushort)payload.Length);

            var data = new byte[6 + payload.Length + 2];
            data[0] = 0x51; // Magic Number
            data[1] = 0x78; // Magic Number
            data[2] = (byte)command; // Command ID and Direction
            data[3] = 0x00; // Command ID and Direction
            Array.Copy(payloadLength, 0, data, 4, 2); // Payload length
            Array.Copy(payload, 0, data, 6, payload.Length); // Payload
            data[data.Length - 2] = crc.Value; // CRC
            data[data.Length - 1] = 0xff; // footer


            // Send Data
            await Write(_service.Writer, data);
        }

        protected byte[] Compress(byte[] plain)
        {
            return SharpLzo.Lzo.Compress(plain);
        }


        public async Task Read()
        {
            if (null == _service)
                throw new Exception("프린터가 준비되지 않았습니다.");

            if (null == _service.Reader)
                throw new Exception("Reader 서비스가 준비되지 않았습니다.");


            // Reader에서 첫 번째 특성 가져오기
            var characteristic = _service.Reader;

            // Notify 또는 Indicate 속성이 있는 경우 구독 설정
            if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify) ||
                characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
            {
                // ValueChanged 이벤트 핸들러 등록
                characteristic.ValueChanged += (sender, args) =>
                {
                    var reader = Windows.Storage.Streams.DataReader.FromBuffer(args.CharacteristicValue);
                    var data = new byte[reader.UnconsumedBufferLength];
                    reader.ReadBytes(data);
                    
                    // 데이터 처리 (예: 로그 출력)
                    Console.WriteLine($"Received data: {BitConverter.ToString(data)}");
                };

                // CCCD (Client Characteristic Configuration Descriptor) 설정
                var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);

                if (status != GattCommunicationStatus.Success)
                    throw new Exception($"Notify 구독 설정 실패: {status}");
            }

            // Read 속성이 있는 경우 직접 읽기
            if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
            {
                var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                
                if (result.Status != GattCommunicationStatus.Success)
                    throw new Exception($"데이터 읽기 실패: {result.Status}");

                var reader = Windows.Storage.Streams.DataReader.FromBuffer(result.Value);
                var data = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(data);

                Console.WriteLine($"Read data: {BitConverter.ToString(data)}");
            }
        }


        public enum Command : byte
        {
            FeedPaper = 0xA1, // LE U16, pixels of paper to feed
            SetFeedSpeed = 0xBD, // U8, speed divisor (smaller is faster)
            Print = 0xBE, // U8, print type OR { U8, print type; U8, grayscale depth }
            Quality = 0xA4, // U8, quality
            Energy = 0xAF, // LE U16, thermal printhead energy
            DeviceStatus = 0xAE, // See below
            GrayCompressedScanlineData = 0xCF,  // See below
            BinaryCompressedScanlineData = 0xCE, // See below
            BinaryRawSingleScanline = 0xA2, // See below
            BinaryCompressedSingleScanline = 0xBF, // Unknown
            Lattice = 0xA6, // Unknown
            DeviceInfo = 0xA8, // Unknown
            DeviceID = 0xBB, // Unknown
            DeviceState = 0xA3, // Unknown
            UpdateDevice = 0xA9, // Unknown
            BatteryLevel = 0xBA // Unknown
        }

        public enum PrintType : byte
        {
            Image = 0x00,
            Text = 0x01,
            Tattoo = 0x02,
            Label = 0x03
        }

        public enum PrintQuality : byte
        {
            Draft = 0x31,
            Low = 0x32,
            Normal = 0x33,
            High = 0x02,
            Best = 0x35,
        }

        public enum PrintBitDepth : byte
        {
            Gray8 = 0x00,
            Gray16 = 0x01,
        }

        public class PrinterGattService
        {
            public GattCharacteristic? Writer { get; set; }
            public GattCharacteristic? Reader { get; set; }
        }
    }
}