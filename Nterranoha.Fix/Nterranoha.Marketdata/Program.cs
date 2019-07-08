using System;

namespace Nterranoha.Marketdata
{
    class Program
    {
        static void Main(string[] args)
        {
            System.Console.WriteLine("=============");
            System.Console.WriteLine("This is only an example program.");
            System.Console.WriteLine();
            System.Console.WriteLine("                                                    ! ! !");
            System.Console.WriteLine("              DO NOT USE THIS ON A COMMERCIAL FIX INTERFACE!");
            System.Console.WriteLine("                                                    ! ! !");
            System.Console.WriteLine();
            System.Console.WriteLine("=============");

            try
            {
                QuickFix.SessionSettings settings = new QuickFix.SessionSettings("market-data.cfg");
                MarketDataApp application = new MarketDataApp(settings.Get().GetString("LogonName"), settings.Get().GetString("LogonPassword"));
                QuickFix.IMessageStoreFactory storeFactory = new QuickFix.FileStoreFactory(settings);
                QuickFix.ILogFactory logFactory = new QuickFix.FileLogFactory(settings);
                QuickFix.Transport.SocketInitiator initiator = new QuickFix.Transport.SocketInitiator(application, storeFactory, settings, logFactory);

                // this is a developer-test kludge.  do not emulate.
                application.MyInitiator = initiator;

                initiator.Start();
                application.Run();
                initiator.Stop();
            }
            catch (System.Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
        }
    }
}
