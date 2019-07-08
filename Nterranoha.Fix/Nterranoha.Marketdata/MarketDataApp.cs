using System;
using QuickFix;
using QuickFix.Fields;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Nterranoha.Marketdata
{
    public class MarketDataApp : QuickFix.MessageCracker, QuickFix.IApplication
    {
        List<Session> _sessions = new List<Session>();

        // This variable is a kludge for developer test purposes.  Don't do this on a production application.
        public IInitiator MyInitiator = null;

        private string _logonName = default(string);
        private string _logonPassword = default(string);
        public MarketDataApp():base()
        {

        }
        public MarketDataApp(string logonName, string logonPassword):base()
        {
            this._logonName = logonName;
            this._logonPassword = logonPassword;
        }
        #region IApplication interface overrides

        public void OnCreate(SessionID sessionID)
        {
            this._sessions.Add( Session.LookupSession(sessionID) );
        }

        public void OnLogon(SessionID sessionID) { Console.WriteLine("Logon - " + sessionID.ToString()); }
        public void OnLogout(SessionID sessionID) { Console.WriteLine("Logout - " + sessionID.ToString()); }

        public void FromAdmin(Message message, SessionID sessionID) { }
        public void ToAdmin(Message message, SessionID sessionID) {
            if (message.Header.GetString(35) == MsgType.LOGON)
            {
                message.SetField(new Username(this._logonName));
                message.SetField(new Password(this._logonPassword));
            }
        }

        public void FromApp(Message message, SessionID sessionID)
        {
            //Console.WriteLine("IN:  " + message.ToString());
            try
            {
                Crack(message, sessionID);
            }
            catch (Exception ex)
            {
                Console.WriteLine("==Cracker exception==");
                Console.WriteLine(ex.ToString());
                Console.WriteLine(ex.StackTrace);
            }
        }

        public void ToApp(Message message, SessionID sessionID)
        {
            try
            {
                bool possDupFlag = false;
                if (message.Header.IsSetField(QuickFix.Fields.Tags.PossDupFlag))
                {
                    possDupFlag = QuickFix.Fields.Converters.BoolConverter.Convert(
                        message.Header.GetField(QuickFix.Fields.Tags.PossDupFlag)); /// FIXME
                }
                if (possDupFlag)
                    throw new DoNotSend();
            }
            catch (FieldNotFoundException)
            { }

            Console.WriteLine();
            Console.WriteLine("OUT: " + message.ToString());
        }
        #endregion


        #region MessageCracker handlers
      
        public void OnMessage(QuickFix.FIX44.MarketDataIncrementalRefresh m, SessionID s)
        {
            QuickFix.FIX44.MarketDataIncrementalRefresh.NoMDEntriesGroup group= new QuickFix.FIX44.MarketDataIncrementalRefresh.NoMDEntriesGroup();
            m.GetGroup(1, group);
            var quote = new
            {
                Side= (group.MDEntryType.getValue() == MDEntryType.BID ? "Bid" : "Offer"),
                LP = group.MDEntryOriginator.getValue(),
                Security = group.MDEntryOriginator.getValue(),
                Price = group.MDEntryPx.getValue(),
                Quantity = group.MDEntrySize.getValue(),
                UpdateAction = group.MDUpdateAction.getValue()
            };
            var trace = JsonConvert.SerializeObject(quote);
            Console.WriteLine(trace);
            System.IO.File.AppendAllText("trace.log", trace+"\r\n");
        }
        #endregion


        public void Run()
        {
            while (true)
            {
                try
                {
                    char action = QueryAction();
                    if (action == '1')
                        QueryMarketDataRequest();
                    else if (action == 'g')
                    {
                        if (this.MyInitiator.IsStopped)
                        {
                            Console.WriteLine("Restarting initiator...");
                            this.MyInitiator.Start();
                        }
                        else
                            Console.WriteLine("Already started.");
                    }
                    else if (action == 'x')
                    {
                        if (this.MyInitiator.IsStopped)
                            Console.WriteLine("Already stopped.");
                        else
                        {
                            Console.WriteLine("Stopping initiator...");
                            this.MyInitiator.Stop();
                        }
                    }
                    else if (action == 'q' || action == 'Q')
                        break;
                }
                catch (System.Exception e)
                {
                    Console.WriteLine("Message Not Sent: " + e.Message);
                    Console.WriteLine("StackTrace: " + e.StackTrace);
                }
            }
           
        }

        private void SendMessage(Message m, string pattern)
        {
            foreach( var session in this._sessions)
            {
                if( session.SessionID.SenderCompID.EndsWith(pattern))
                {
                    session.Send(m);
                }
            }
        }

        private char QueryAction()
        {
            // Commands 'g' and 'x' are intentionally hidden.
            Console.Write("\n"
                + "1) Market data\n"
                + "Q) Quit\n"
                + "Action: "
            );

            HashSet<string> validActions = new HashSet<string>("1,4,q,Q,g,x".Split(','));

            string cmd = Console.ReadLine().Trim();
            if (cmd.Length != 1 || validActions.Contains(cmd) == false)
                throw new System.Exception("Invalid action");

            return cmd.ToCharArray()[0];
        }


        private void QueryMarketDataRequest()
        {
           
            QuickFix.FIX44.MarketDataRequest result = new QuickFix.FIX44.MarketDataRequest();
            result.SetField(new MDReqID(DateTime.Now.Millisecond.ToString()));

            var noContraBrokersGroup = new QuickFix.FIX44.ExecutionReport.NoContraBrokersGroup();
            noContraBrokersGroup.Set(QueryPool());
            result.AddGroup(noContraBrokersGroup);

            QuickFix.FIX44.MarketDataRequest.NoRelatedSymGroup symGroup = new QuickFix.FIX44.MarketDataRequest.NoRelatedSymGroup();
            symGroup.SetField(QuerySymbol());
            symGroup.SetField(new MDUpdateType(MDUpdateType.INCREMENTAL_REFRESH));
            symGroup.SetField(new MarketDepth(0));
            symGroup.SetField(new SubscriptionRequestType(SubscriptionRequestType.SNAPSHOT_PLUS_UPDATES));

            QuickFix.FIX44.MarketDataRequest.NoMDEntryTypesGroup typesGroup = new QuickFix.FIX44.MarketDataRequest.NoMDEntryTypesGroup();
            typesGroup.SetField(new MDEntryType(MDEntryType.BID));
            result.AddGroup(typesGroup);

            typesGroup = new QuickFix.FIX44.MarketDataRequest.NoMDEntryTypesGroup();
            typesGroup.SetField(new MDEntryType(MDEntryType.OFFER));
            result.AddGroup(symGroup);

            Console.WriteLine(result);

            SendMessage(result, ".MD");
        }


        #region Message creation functions

        private Symbol QuerySymbol()
        {
            Console.WriteLine();
            Console.WriteLine("Symbol? ");
            Console.WriteLine("1) EURUSD");
            Console.WriteLine("2) USDJPY");
            Console.WriteLine("3) GBPUSD");
            Console.WriteLine("4) AUDUSD");
            Console.WriteLine("5) USDCHF");
            Console.WriteLine("6) NZDUSD");
            Console.WriteLine("7) USDCAD");
            string s = Console.ReadLine().Trim();

            string symbol = string.Empty;
            switch (s)
            {
                case "1": symbol = "EURUSD"; break;
                case "2": symbol = "USDJPY"; break;
                case "3": symbol = "GBPUSD"; break;
                case "4": symbol = "AUDUSD"; break;
                case "5": symbol = "USDCHF"; break;
                case "6": symbol = "NZDUSD"; break;
                case "7": symbol = "USDCAD"; break;
                default: throw new Exception("unsupported input");
            }
            return new Symbol(symbol);
        }

        private ContraBroker QueryPool()
        {
            
            Console.WriteLine();
            Console.WriteLine("Pool? ");
            string s = Console.ReadLine().Trim();

            return new ContraBroker(s);

        }
            #endregion
        }
}
