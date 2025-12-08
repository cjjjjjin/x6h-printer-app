using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth;



var printer = new bluetoothTest.Printer();
printer.Prepare();
printer.FeedPaper(100);
//await printer.GetDeviceStatusAsync();
