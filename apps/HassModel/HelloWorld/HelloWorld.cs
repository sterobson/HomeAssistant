//// Use unique namespaces for your apps if you going to share with others to avoid
//// conflicting names

//using HomeAssistant.apps;
//using HomeAssistant.apps.Energy;
//using HomeAssistantGenerated;
//using System.Threading.Tasks;

//namespace HassModel;

///// <summary>
/////     Hello world showcase using the new HassModel API
///// </summary>
//[NetDaemonApp]
//public class HelloWorldApp
//{
//    public HelloWorldApp(IHaContext ha, ITriggerManager triggerManager, IElectricityRatesReader electricityRates)
//    {
//        ha.CallService("notify", "persistent_notification", data: new { message = "Notify me", title = "Hello world!" });

//        Entities entities = new Entities(ha);
//        MyDevices myDevices = new MyDevices(entities, ha);

//        myDevices.DiningRoomDeskButton.Pressed().SubscribeAsync(async e =>
//        {
//            int i = 0;
//            Random random = new();
//            while (i++ < 30)
//            {
//                myDevices.GamesRoomDeskLamp
//                    .SetRgb(random.Next(0, 256), random.Next(0, 256), random.Next(0, 256))
//                    .SetBrightnessPercent(50);
//                await Task.Delay(100);
//            }

//            myDevices.GamesRoomDeskLamp.TurnOn(effect: "Warm white");
//        });

//        myDevices.GamesRoomDeskTemperature.StateChanges().SubscribeAsync(async e =>
//        {
//            Console.WriteLine($"{e.Entity.Attributes?.FriendlyName} set to {e.New?.State}{e.Entity.Attributes?.UnitOfMeasurement}");
//        });

//        myDevices.GamesRoomDeskHumidity.StateChanges().SubscribeAsync(async e =>
//        {
//            Console.WriteLine($"{e.Entity.Attributes?.FriendlyName} set to {e.New?.State}{e.Entity.Attributes?.UnitOfMeasurement}");
//        });

//        EnergyRate rate = electricityRates.GetCurrentElectricityImportRateAsync().GetAwaiter().GetResult();
//    }

//}