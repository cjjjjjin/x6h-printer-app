using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth;
using bluetoothTest;
using Windows.System.Profile;


/*
var printer = new bluetoothTest.Printer();
printer.Prepare();
printer.FeedPaper(100);*/


/*
var printer = new bluetoothTest.Printer();
printer.Prepare();
var ret = ImageGenerator.CreateBitmapWithWrappedText8Bit(
    "This is a sample text that will be wrapped within the specified width.",
    fontFamily: "Arial", fontSize: 24,
    width: 384, padding: 10,
    textColor:0, backgroundColor:255);
var bit4image = ImageGenerator.Convert8BitTo4Bit(ret.PixelData, ret.Width, ret.Height);
printer.Draw4BitGrayImage(bit4image);
*/

var searcher = new BluetoothSearcher();
searcher.DeviceDiscovered += (sender, device) => {
    Console.WriteLine($"장비 발견: {device.LocalName}");
};
searcher.StartScanning();

// 스캔 후
await Task.Delay(5000);
var devices = searcher.GetDiscoveredDevices();
searcher.StopScanning();