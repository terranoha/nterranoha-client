using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using QuickFix;
using QuickFix.Fields;

namespace Nterranoha.Execution
{
    public class TradingApp : QuickFix.MessageCracker, QuickFix.IApplication
    {

        List<Session> _sessions = new List<Session>();

        // This variable is a kludge for developer test purposes.  Don't do this on a production application.
        public IInitiator MyInitiator = null;

        private Dictionary<string, DateTime> _orderHash = new Dictionary<string, DateTime>();
        private string _logonName = default(string);
        private string _logonPassword = default(string);
        public TradingApp() : base()
        {

        }
        public TradingApp(string logonName, string logonPassword) : base()
        {
            this._logonName = logonName;
            this._logonPassword = logonPassword;
        }
        #region IApplication interface overrides

        public void OnCreate(SessionID sessionID)
        {
            this._sessions.Add(Session.LookupSession(sessionID));
        }

        public void OnLogon(SessionID sessionID) { Console.WriteLine("Logon - " + sessionID.ToString()); }
        public void OnLogout(SessionID sessionID) { Console.WriteLine("Logout - " + sessionID.ToString()); }

        public void FromAdmin(Message message, SessionID sessionID) { }
        public void ToAdmin(Message message, SessionID sessionID)
        {
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

        public void OnMessage(QuickFix.FIX44.ExecutionReport m, SessionID s)
        {
            var stringBuilder = new StringBuilder("***Execution report***:\r\n");
            stringBuilder.Append("================\r\n");
            for (int i = 1; i <= m.GetInt(QuickFix.Fields.Tags.NoContraBrokers); i++)
            {   
                var group = new QuickFix.FIX44.ExecutionReport.NoContraBrokersGroup();
                m.GetGroup(i, group);
                stringBuilder.Append("LP:").Append(group.ContraBroker.getValue()).Append("\r\n");
            }
            stringBuilder.Append("Price:").Append(m.AvgPx.getValue()).Append("\r\n");
            stringBuilder.Append("Side:").Append(m.Side.getValue()=='1'?"Buy" :"Sell").Append("\r\n");
            stringBuilder.Append("Quantity:").Append(m.OrderQty.getValue()).Append("\r\n");

            var TimeIn = default(DateTime);
            var TimeOut = default(DateTime);
            var BrokerReceipt = default(DateTime);
            var BrokerExec = default(DateTime);
            for ( int i = 1; i<= m.GetInt(QuickFix.Fields.Tags.NoTrdRegTimestamps); i++)
            {
                var group = new QuickFix.FIX44.CollateralReport.NoTrdRegTimestampsGroup();
                m.GetGroup(i, group);
                switch( i)
                {
                    case 1:
                        TimeIn = group.TrdRegTimestamp.getValue();
                        break;
                    case 2:
                        TimeOut = group.TrdRegTimestamp.getValue();
                        break;
                    case 3:
                        BrokerReceipt = group.TrdRegTimestamp.getValue();
                        break;
                    case 4:
                        BrokerExec = group.TrdRegTimestamp.getValue();
                        break;
                }
            }

            stringBuilder.Append("TimeStamps:\r\n");
            stringBuilder.Append("Time in:").Append(TimeIn.ToString("h:m:s.fff")).Append("\r\n");
            stringBuilder.Append("Time out:").Append(TimeOut.ToString("h:m:s.fff")).Append("\r\n");
            stringBuilder.Append("Broker receipt in:").Append(BrokerReceipt.ToString("h:m:s.fff")).Append("\r\n");
            stringBuilder.Append("Broker exec:").Append(BrokerExec.ToString("h:m:s.fff")).Append("\r\n");


            stringBuilder.Append("Customer  |  IdsRoot   |  LP\r\n").Append("\r\n");
            stringBuilder.Append("          |-> Time in  |\r\n").Append("\r\n");
            stringBuilder.Append("          |            |-> Broker Receipt\r\n").Append("\r\n");
            stringBuilder.Append("          |            |<- Broker exec\r\n").Append("\r\n");
            stringBuilder.Append("          |<- Time out |\r\n").Append("\r\n");
            Console.WriteLine(stringBuilder.ToString());
        }

        public void OnMessage(QuickFix.FIX44.OrderCancelReject m, SessionID s)
        {
            Console.WriteLine("Received order cancel reject");
        }

        public void Run()
        {
            while (true)
            {
                try
                {
                    char action = QueryAction();
                    if (action == '1')
                        QueryEnterOrder();
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
            foreach (var session in this._sessions)
            {
                if (session.SessionID.SenderCompID.EndsWith(pattern))
                {
                    session.Send(m);
                }
            }
        }

        private char QueryAction()
        {
            // Commands 'g' and 'x' are intentionally hidden.
            Console.Write("\n"
                + "1) Enter Order\n"
                + "Q) Quit\n"
                + "Action: "
            );

            HashSet<string> validActions = new HashSet<string>("1,4,q,Q,g,x".Split(','));

            string cmd = Console.ReadLine().Trim();
            if (cmd.Length != 1 || validActions.Contains(cmd) == false)
                throw new System.Exception("Invalid action");

            return cmd.ToCharArray()[0];
        }
        private void QueryEnterOrder()
        {
            Console.WriteLine("\nNewOrderSingle");

            QuickFix.FIX44.NewOrderSingle m = QueryNewOrderSingle44();

            if (m != null && QueryConfirm("Send order"))
            {
                m.Header.GetField(Tags.BeginString);
                var transactTime = DateTime.UtcNow;
                this._orderHash.Add(m.ClOrdID.getValue(), transactTime);
                m.TransactTime = new TransactTime(transactTime);
                SendMessage( m , ".OD");
            }
        }

        #region Message creation functions
        private QuickFix.FIX44.NewOrderSingle QueryNewOrderSingle44()
        {
            QuickFix.FIX44.NewOrderSingle newOrderSingle = new QuickFix.FIX44.NewOrderSingle();
            newOrderSingle.ClOrdID = new ClOrdID(DateTime.Now.Millisecond.ToString());
            var noContraBrokersGroup = new QuickFix.FIX44.ExecutionReport.NoContraBrokersGroup();
            noContraBrokersGroup.Set(QueryPool());
            newOrderSingle.AddGroup(noContraBrokersGroup);
            newOrderSingle.Symbol = QuerySymbol();
            newOrderSingle.Side = QuerySide();
            newOrderSingle.OrdType = new OrdType(OrdType.MARKET);
            newOrderSingle.Set(QueryOrderQty());
            newOrderSingle.Set(new TimeInForce(TimeInForce.FILL_OR_KILL));
            return newOrderSingle;
        }
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


        private Side QuerySide()
        {
            Console.WriteLine();
            Console.WriteLine("Side? ");
            Console.WriteLine("1) Buy");
            Console.WriteLine("2) Sell");
            string s = Console.ReadLine().Trim();

            char c = ' ';
            switch (s)
            {
                case "1": c = Side.BUY; break;
                case "2": c = Side.SELL; break;
                default: throw new Exception("unsupported input");
            }
            return new Side(c);
        }

        private OrderQty QueryOrderQty()
        {
            Console.WriteLine();
            Console.Write("OrderQty? ");
            return new OrderQty(Convert.ToDecimal(Console.ReadLine().Trim()));
        }
        private bool QueryConfirm(string query)
        {
            Console.WriteLine();
            Console.WriteLine(query + "?: (y/N) ");
            string line = Console.ReadLine().Trim();
            return (line[0].Equals('y') || line[0].Equals('Y'));
        }
        private ContraBroker QueryPool()
        {

            Console.WriteLine();
            Console.WriteLine("Pool name? ");
            string s = Console.ReadLine().Trim();

            return new ContraBroker(s);

        }
        #endregion


    }
}
