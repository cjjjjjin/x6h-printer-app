using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth;



var printer = new bluetoothTest.Printer();
if (!printer.FindPrinter())
{
    Console.WriteLine("프린터 장치를 찾지 못했습니다.");
    return;
}

Console.WriteLine($"프린터 장치 발견! 장치 ID: {printer.TargetDeviceID.Value}");

// 서비스 목록 확인
Console.WriteLine("\n서비스 목록 조회 중...");
var services = await printer.GetServicesAsync();

if (services.Count == 0)
{
    Console.WriteLine("서비스를 찾지 못했습니다.");
}
else
{
    Console.WriteLine($"총 {services.Count}개의 서비스 발견:\n");
    foreach (var service in services)
    {
        Console.WriteLine($"서비스 UUID: {service.Uuid}");
        
        // 각 서비스의 Characteristic 목록도 출력
        var characteristics = await service.GetCharacteristicsAsync();
        if (characteristics.Status == Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success)
        {
            foreach (var characteristic in characteristics.Characteristics)
            {
                Console.WriteLine($"  - Characteristic UUID: {characteristic.Uuid}");
                Console.WriteLine($"    Properties: {characteristic.CharacteristicProperties}");
            }
        }
        Console.WriteLine();
    }
}


