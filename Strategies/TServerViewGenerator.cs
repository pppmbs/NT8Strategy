#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.IO;
using System.Net.Sockets;
using System.Net;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class TServerViewGenerator : Strategy
    {
        private string pathMktView;
        private StreamWriter swMkt = null; // Store market view, 0=Bear, 1=Neutral, 2=Bull

        private string pathExitView;
        private StreamWriter swExit = null; // Store exit view, 0=Sell, 1=Hold, 2=Buy

        // log, error, current capital, profit percentage for early exit, market view and vix  files
        private string pathLog;
        private string pathErr;
        private StreamWriter swLog = null; // runtime log file
        private StreamWriter swErr = null; // error file

        // Macro Market Views
        enum MarketView
        {
            Buy,
            Sell,
            Hold
        };
        MarketView currMarketView = MarketView.Hold;

        // Macro Exit Views
        enum ExitView
        {
            Buy,
            Sell,
            Hold
        };
        ExitView currExitView = ExitView.Hold;

        private static bool UseTServerFilters = true;
        private static bool UseMomentumFilter = true;

        private Socket tSender = null;
        private byte[] tBytes = new byte[1024];
        int tLineNo = 0;
        private static readonly int tPortNumber = 3883;
        private static readonly string hostName = Dns.GetHostName();
        private string tServerSignal = "1";

        enum ErrorType
        {
            verbose,
            normal,
            warning,
            fatal
        };
        private static ErrorType defaultErrorType = ErrorType.verbose;

        // CloseStrategy() is called in the event of a fatal error, which will close all positions and disable strategy
        private void MyErrPrint(ErrorType errType, string buf)
        {
            string errString = "";

            if (swErr == null)
            {
                pathErr = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
                //pathErr = System.IO.Path.Combine(pathErr, Dns.GetHostName() + "-" + PortNumber.ToString() + "-" + DateTime.Today.ToString("yyyyMMdd") + ".err");
                pathErr = System.IO.Path.Combine(pathErr, hostName + "-" + tPortNumber.ToString() + "-" + DateTime.Today.ToString("yyyyMMdd") + ".err");
                swErr = File.AppendText(pathErr);  // Open the path for err file writing
            }

            switch (errType)
            {
                case ErrorType.fatal:
                    errString = "FATAL: ";
                    break;
                case ErrorType.warning:
                    errString = "WARNING: ";
                    break;
            }

            swErr.WriteLine(errString + DateTime.Now + " " + buf); // Append a new line to the err file

            // close error file
            swErr.Close();
            swErr.Dispose();
            swErr = null;

            MyPrint(errType, errString + DateTime.Now + " " + buf); // replicate error message to log file

            // Cancels all working orders, closes any existing positions, and finally disables the strategy.
            if (errType == ErrorType.fatal)
            {
                CloseStrategy(errString);
            }
        }

        private void MyPrint(ErrorType errType, string buf)
        {
            if (swLog == null)
            {
                //Create log file in the PortNumber-yyyyMMdd.log format
                pathLog = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
                //pathLog = System.IO.Path.Combine(pathLog, Dns.GetHostName() + "-" + PortNumber.ToString() + "-" + DateTime.Today.ToString("yyyyMMdd") + ".log");
                pathLog = System.IO.Path.Combine(pathLog, hostName + "-" + tPortNumber.ToString() + "-" + DateTime.Today.ToString("yyyyMMdd") + ".log");
                swLog = File.AppendText(pathLog);  // Open the path for log file writing
            }

            swLog.WriteLine(DateTime.Now + " " + buf); // Append a new line to the log file

            // only print out verbose, warning and fatal messages to output screen
            if (errType != ErrorType.normal)
            {
                if (errType == ErrorType.warning || errType == ErrorType.verbose)
                    //Set this scripts MyPrint() calls to the first output tab
                    PrintTo = PrintTo.OutputTab2;
                if (errType == ErrorType.fatal)
                    //Set this scripts MyPrint() calls to the second output tab
                    PrintTo = PrintTo.OutputTab2;

                //Print(HostName + ":" + PortNumber.ToString() + ":" + DateTime.Now + " " + buf);
                Print(tPortNumber.ToString() + ":" + DateTime.Now.ToString("HHmmss") + " " + buf);
            }


            swLog.Close();
            swLog.Dispose();
            swLog = null;
        }

        private void ConnectTimeServer()
        {
            // Connect to Time Server  
            try
            {
                // Do not attempt connection if already connected
                if (tSender != null)
                    return;

                // Establish the remote endpoint for the socket.  
                // connecting server on vtPortNumber  
                IPHostEntry ipHostInfo = Dns.GetHostEntry(hostName);

                foreach (IPAddress ip in ipHostInfo.AddressList)
                {
                    IPAddress ipv4;

                    ipv4 = ip.MapToIPv4();
                    MyPrint(defaultErrorType, "ipv4= " + ipv4.ToString());
                }

                IPAddress ipAddress = ipHostInfo.AddressList[1]; // depending on the Wifi set up, this index may change accordingly
                                                                 //IPAddress ipAddress = ipHostInfo.AddressList[3];
                                                                 //ipAddress = ipAddress.MapToIPv4();
                IPEndPoint remoteEP = new IPEndPoint(ipAddress, tPortNumber);

                MyPrint(defaultErrorType, "ipHostInfo=" + ipHostInfo.ToString() + " ipAddress=" + ipAddress.ToString());

                // Create a TCP/IP  socket.  
                tSender = new Socket(ipAddress.AddressFamily,
                    SocketType.Stream, ProtocolType.Tcp);

                // Connect the socket to the remote endpoint. Catch any errors.  
                try
                {
                    tSender.Connect(remoteEP);

                    MyPrint(defaultErrorType, " ************ Socket connected to : " +
                        tSender.RemoteEndPoint.ToString() + "*************");

                    // set receive timeout 10 secs
                    tSender.ReceiveTimeout = 10000;
                    // set send timeout 10 secs
                    tSender.SendTimeout = 10000;
                }
                catch (ArgumentNullException ane)
                {
                    MyErrPrint(ErrorType.fatal, "Socket Connect Error: ArgumentNullException : " + ane.ToString());
                }
                catch (SocketException se)
                {
                    MyErrPrint(ErrorType.fatal, "Socket Connect Error: SocketException : " + se.ToString());
                }
                catch (Exception e)
                {
                    MyErrPrint(ErrorType.fatal, "Socket Connect Error: Unexpected exception : " + e.ToString());
                }
            }
            catch (Exception e)
            {
                MyErrPrint(ErrorType.fatal, e.ToString());
            }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"T-Server provides market views for Buy, Sell or Hold";
                Name = "TServerViewGenerator";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                // Disable this property for performance gains in Strategy Analyzer optimizations
                // See the Help Guide for additional information
                IsInstantiatedOnEachOptimizationIteration = true;
            }
            else if (State == State.Configure)
            {
                // Output tab2
                PrintTo = PrintTo.OutputTab2;
            }
            else if (State == State.DataLoaded)
            {
                MyPrint(defaultErrorType, "State == State.DataLoaded");

                ConnectTimeServer();
            }
        }


        private void WriteMarketView(MarketView mktView)
        {
            pathMktView = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
            pathMktView = System.IO.Path.Combine(pathMktView, "Artista" + ".mkt");

            swMkt = File.CreateText(pathMktView); // Open the path for Market View
            switch (mktView)
            {
                case MarketView.Buy:
                    swMkt.WriteLine("2");
                    break;
                case MarketView.Sell:
                    swMkt.WriteLine("0");
                    break;
                default:
                    swMkt.WriteLine("1");
                    break;
            }
            swMkt.Close();
            swMkt.Dispose();
            swMkt = null;
        }

        private void WriteExitView(ExitView exitView)
        {
            pathExitView = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
            pathExitView = System.IO.Path.Combine(pathExitView, "Artista" + ".xit");

            swExit = File.CreateText(pathExitView); // Open the path for Exit View
            switch (exitView)
            {
                case ExitView.Buy:
                    swExit.WriteLine("2");
                    break;
                case ExitView.Sell:
                    swExit.WriteLine("0");
                    break;
                default:
                    swExit.WriteLine("1");
                    break;
            }
            swExit.Close();
            swExit.Dispose();
            swExit = null;
        }

        private bool BollingerFlat()
        {


            // if Bollinger width is less than 5 then return true (Hold)
            if ((Bollinger(2, 20).Upper[0] - Bollinger(2, 20).Lower[0]) < 5)
            {
                MyPrint(defaultErrorType, "Bollinger width= " + (Bollinger(2, 20).Upper[0] - Bollinger(2, 20).Lower[0]).ToString());
                return true;
            }

            return false;
        }


        private bool BollingerWrongTrend(char signal)
        {
            double lastMidBoll;
            double currMidBoll;

            currMidBoll = (Bollinger(2, 20).Upper[0] + Bollinger(2, 20).Lower[0]) / 2;
            lastMidBoll = (Bollinger(2, 20).Upper[1] + Bollinger(2, 20).Lower[1]) / 2;
            MyPrint(defaultErrorType, "currMidBoll =" + currMidBoll.ToString() + " lastMidBoll =" + lastMidBoll.ToString());

            switch (signal)
            {
                case '0':
                    // if mid bollinger is trending up, return true
                    if (currMidBoll >= lastMidBoll)
                        return true;
                    break;

                case '2':
                    // if mid bollinger is trending down, return true
                    if (currMidBoll <= lastMidBoll)
                        return true;
                    break;

                default:
                    return false;
            }

            return false;
        }


        // return True if volume too low
        private bool VolumeTooLow()
        {
            // if the trading in the last 5 minutes is less than 5000, return true
            if (Bars.GetVolume(CurrentBar) < 5000)
            {
                MyPrint(defaultErrorType, "Volume= " + Bars.GetVolume(CurrentBar));
                return true;
            }

            return false;
        }


        // return True if momentum too low
        private bool MomentumTooLow()
        {
            if (Momentum(20)[0] > -1)
                return false;
            if (Momentum(20)[0] > -2)
            {
                if (Bars.GetClose(CurrentBar) < ((Bollinger(2, 20).Upper[0] + Bollinger(2, 20).Lower[0]) / 2))
                    return false;
            }
            return true;
        }


        private bool OverBoughtOverSold(char signal)
        {
            switch (signal)
            {
                case '0':
                    if (Bars.GetHigh(CurrentBar) != Bars.GetClose(CurrentBar))
                        return true;
                    break;

                case '2':
                    if (Bars.GetHigh(CurrentBar) == Bars.GetClose(CurrentBar))
                        return true;
                    break;

                default:
                    return false;
            }
            return false;
        }


        private bool OpenCloseWrongDirection(char signal)
        {
            switch (signal)
            {
                case '0':
                    if (Bars.GetOpen(CurrentBar) < Bars.GetClose(CurrentBar))
                        return true;
                    break;

                case '2':
                    if (Bars.GetOpen(CurrentBar) > Bars.GetClose(CurrentBar))
                        return true;
                    break;

                default:
                    return false;
            }
            return false;
        }


        // returns false if failed the check
        private bool CheckSMA50MarketDirection(char signal)
        {
            bool SMA50TrendingUp = SMA(50)[0] > SMA(50)[1];

            MyPrint(defaultErrorType, "CheckSMA50MarketDirection= @@" + SMA50TrendingUp + " @@");

            switch (signal)
            {
                case '0':
                    if (!SMA50TrendingUp)
                        return true;
                    break;
                case '2':
                    if (SMA50TrendingUp)
                        return true;
                    break;
            }
            return false;
        }

        // returns false if failed the check
        private bool CheckSMA20MarketDirection(char signal)
        {
            bool SMA20TrendingUp = SMA(20)[0] > SMA(20)[1];

            MyPrint(defaultErrorType, "SMA20TrendingUp= @@" + SMA20TrendingUp + " @@");

            switch (signal)
            {
                case '0':
                    if (!SMA20TrendingUp)
                        return true;
                    break;
                case '2':
                    if (SMA20TrendingUp)
                        return true;
                    break;
            }
            return false;
        }


        // returns false if failed the check
        private bool CheckSMA9MarketDirection(char signal)
        {
            bool SMA9TrendingUp = SMA(9)[0] > SMA(9)[1];

            MyPrint(defaultErrorType, "SMA9TrendingUp= @@ " + SMA9TrendingUp + " @@");

            switch (signal)
            {
                case '0':
                    if (!SMA9TrendingUp)
                        return true;
                    break;
                case '2':
                    if (SMA9TrendingUp)
                        return true;
                    break;
            }
            return false;
        }


        private bool CheckSMA9MarketDirection2X(char signal)
        {
            bool SMA9TrendingUp1 = SMA(9)[0] > SMA(9)[1];
            bool SMA9TrendingUp2 = SMA(9)[1] > SMA(9)[2];

            MyPrint(defaultErrorType, "CheckSMA9MarketDirection2X= @@ " + SMA9TrendingUp1 + ":" + SMA9TrendingUp2 + " @@");

            switch (signal)
            {
                case '0':
                    if (!(SMA9TrendingUp1 && SMA9TrendingUp2))
                        return true;
                    break;
                case '2':
                    if (SMA9TrendingUp1 && SMA9TrendingUp2)
                        return true;
                    break;
            }
            return false;
        }


        // Different filtering mechanism employed for the T-Server signals, if anyone of them returned true, T-Server will be set to Hold
        private bool FilterTServer(char signal)
        {
            CheckSMA9MarketDirection(signal);
            CheckSMA9MarketDirection2X(signal);
            CheckSMA20MarketDirection(signal);
            CheckSMA50MarketDirection(signal);

            /*
            if (BollingerFlat())
                        {
            MyPrint(defaultErrorType, "BollingerFlat is TRUE, set T-Server to Hold!");
            return true;
            }

            if (BollingerWrongTrend(signal))
                        {
            MyPrint(defaultErrorType, "BollingerWrongTrend is TRUE, set T-Server to Hold!");
            return true;
            }

            if (VolumeTooLow())
                        {
            MyPrint(defaultErrorType, "VolumeTooLow is TRUE, set T-Server to Hold!");
            return true;
            }
            */

            if (signal == '0')
            {
                PlaySound(@"C:\Program Files\NinjaTrader 8\sounds\glass_shatter_c.wav");
                MyPrint(defaultErrorType, "===========+++++++++++++ {{{  UNFILTERED SELL Signals  }}} ++++++++++++=============");
            }

            if (signal == '2')
            {
                PlaySound(@"C:\Program Files\NinjaTrader 8\sounds\short-horn.wav");
                MyPrint(defaultErrorType, "===========+++++++++++++ {{{  UNFILTERED BUY Signals  }}} ++++++++++++=============");
            }

            if (OverBoughtOverSold(signal))
            {
                MyPrint(defaultErrorType, "OverBoughtOverSold is TRUE, set T-Server to Hold!");
                return true;
            }

            if (OpenCloseWrongDirection(signal))
            {
                MyPrint(defaultErrorType, "OpenCloseWrongDirection is TRUE, set T-Server to Hold!");
                return true;
            }

            if (UseMomentumFilter && MomentumTooLow())
            {
                MyPrint(defaultErrorType, "MomentumTooLow is TRUE, set T-Server to Hold!");
                return true;
            }

            return false;
        }

        private void CheckMarketWarnings()
        {
            //if ((Bollinger(2, 20).Upper[0] - Bollinger(2, 20).Lower[0]) <= 10)
            //{
            //    PlaySound(@"C:\Program Files\NinjaTrader 8\sounds\boxing_bell.wav");
            //}
            if (Momentum(20)[0] <= 0 || Bars.GetVolume(CurrentBar) <= 10000 || ((Bollinger(2, 20).Upper[0] - Bollinger(2, 20).Lower[0]) <= 10))
            {
                PlaySound(@"C:\Program Files\NinjaTrader 8\sounds\glass_shatter_c.wav");
            }
        }

        private void SetTServerSignals(char signal)
        {
            // if T-Server filter returns true, set to Hold
            if (UseTServerFilters && FilterTServer(signal))
            {
                currMarketView = MarketView.Hold;
                PlaySound(@"C:\Program Files\NinjaTrader 8\sounds\ding.wav");
            }
            else
            {
                CheckMarketWarnings();

                switch (signal)
                {
                    case '0':
                        currMarketView = MarketView.Sell;
                        PlaySound(@"C:\Program Files\NinjaTrader 8\sounds\glass_shatter_c.wav");
                        break;
                    case '2':
                        currMarketView = MarketView.Buy;
                        PlaySound(@"C:\Program Files\NinjaTrader 8\sounds\short-horn.wav");
                        PlaySound(@"C:\Program Files\NinjaTrader 8\sounds\short-horn.wav");
                        break;
                    default:
                        currMarketView = MarketView.Hold;
                        PlaySound(@"C:\Program Files\NinjaTrader 8\sounds\ding.wav");
                        break;
                }
            }
        }

        private bool FilterExitSignals(char signal)
        {
            if (OpenCloseWrongDirection(signal))
            {
                MyPrint(defaultErrorType, "OpenCloseWrongDirection is TRUE, set Exit Signal to Hold!");
                MyPrint(defaultErrorType, "SetExitSignals= [[[ Hold ]]]");
                return true;
            }

            if (signal == '2' && MomentumTooLow())
            {
                MyPrint(defaultErrorType, "MomentumTooLow is TRUE, set Exit Signal to Hold!");
                MyPrint(defaultErrorType, "SetExitSignals= [[[ Hold ]]]");
                return true;
            }

            return false;
        }

        private void SetExitSignals(char signal)
        {
            // if T-Server filter returns true, set to Hold
            if (UseTServerFilters && FilterExitSignals(signal))
            {
                currExitView = ExitView.Hold;
            }
            else
            {
                switch (signal)
                {
                    case '0':
                        MyPrint(defaultErrorType, "SetExitSignals= [[[ Sell ]]]");
                        currExitView = ExitView.Sell;
                        break;
                    case '2':
                        MyPrint(defaultErrorType, "SetExitSignals= [[[ Buy ]]]");
                        currExitView = ExitView.Buy;
                        break;
                    default:
                        MyPrint(defaultErrorType, "SetExitSignals= [[[ Hold ]]]");
                        currExitView = ExitView.Hold;
                        break;
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 0)
            {
                string bufString;

                MyPrint(defaultErrorType, "tLineNo=" + tLineNo.ToString());

                // Skip all previous day bars until second bar of the day
                if (!Bars.GetTime(CurrentBar).Date.ToString("dd/MM/yyyy").Equals(DateTime.Now.ToString("dd/MM/yyyy")))
                    return;
                if (Bars.GetTime(CurrentBar).ToString("HHmm").Equals("0000"))
                    return;

                // construct the string buffer to be sent to DLNN
                bufString = tLineNo.ToString() + ',' +
                    Bars.GetTime(CurrentBar - 1).ToString("HHmmss") + ',' + Bars.GetTime(CurrentBar).ToString("HHmmss") + ',' +
                    Bars.GetOpen(CurrentBar).ToString() + ',' + Bars.GetClose(CurrentBar).ToString() + ',' +
                    Bars.GetHigh(CurrentBar).ToString() + ',' + Bars.GetLow(CurrentBar).ToString() + ',' +
                    Bars.GetVolume(CurrentBar).ToString() + ',' +
                    SMA(9)[0].ToString() + ',' + SMA(20)[0].ToString() + ',' + SMA(50)[0].ToString() + ',' +
                    MACD(12, 26, 9).Diff[0].ToString() + ',' + RSI(14, 3)[0].ToString() + ',' +
                    Bollinger(2, 20).Lower[0].ToString() + ',' + Bollinger(2, 20).Upper[0].ToString() + ',' +
                    CCI(20)[0].ToString() + ',' +
                    Bars.GetHigh(CurrentBar).ToString() + ',' + Bars.GetLow(CurrentBar).ToString() + ',' +
                    Momentum(20)[0].ToString() + ',' +
                    DM(14).DiPlus[0].ToString() + ',' + DM(14).DiMinus[0].ToString() + ',' +
                    VROC(25, 3)[0].ToString() + ',' +
                    '0' + ',' + '0' + ',' + '0' + ',' + '0' + ',' + '0' + ',' +
                    '0' + ',' + '0' + ',' + '0' + ',' + '0' + ',' + '0';

                //MyPrint(defaultErrorType, bufString);

                //MyPrint(defaultErrorType, "CurrentTimeBar = " + CurrentBar + ": " + "bufString = " + bufString);
                if (!Bars.IsFirstBarOfSession)
                {
                    MyPrint(defaultErrorType, "CurrentTimeBar" +
                                " Start time=" + Bars.GetTime(Bars.CurrentBar - 1).ToString("HHmmss") +
                                " End time=" + Bars.GetTime(Bars.CurrentBar).ToString("HHmmss") +
                                " Open=" + Bars.GetOpen(Bars.CurrentBar).ToString() +
                                " Close=" + Bars.GetClose(Bars.CurrentBar).ToString() +
                                " High=" + Bars.GetHigh(Bars.CurrentBar).ToString() +
                                " Low=" + Bars.GetLow(Bars.CurrentBar).ToString() +
                                " Volume=" + Bars.GetVolume(Bars.CurrentBar).ToString() +
                                " SMA9=" + SMA(9)[0].ToString() +
                                " SMA20=" + SMA(20)[0].ToString() +
                                " SMA50=" + SMA(50)[0].ToString() +
                                " MACD=" + MACD(12, 26, 9).Diff[0].ToString() +
                                " RSI=" + RSI(14, 3)[0].ToString() +
                                " Boll_Low=" + Bollinger(2, 20).Lower[0].ToString() +
                                " Boll_Hi=" + Bollinger(2, 20).Upper[0].ToString() +
                                " CCI=" + CCI(20)[0].ToString() +
                                " Momentum=" + Momentum(20)[0].ToString() +
                                " DiPlus=" + DM(14).DiPlus[0].ToString() +
                                " DiMinus=" + DM(14).DiMinus[0].ToString() +
                                " VROC=" + VROC(25, 3)[0].ToString());
                }

                byte[] msg = Encoding.UTF8.GetBytes(bufString);

                int tBytesSent;
                int tBytesRec;

                try
                {
                    // Send the data through the socket.  
                    tBytesSent = tSender.Send(msg);

                    // Receive the response from the remote device.  
                    tBytesRec = tSender.Receive(tBytes);

                    // increment tlineNo for T-Server
                    tLineNo++;
                }
                catch (SocketException ex)
                {
                    MyErrPrint(ErrorType.fatal, "TServer Socket exception::" + ex.Message + " " + ex.ToString());
                }

                tServerSignal = System.Text.Encoding.UTF8.GetString(tBytes, 0, tBytes.Length).Split(',')[1];
                MyPrint(defaultErrorType, "Start time=" + Bars.GetTime(CurrentBar - 1).ToString("HHmmss") + " End time=" + Bars.GetTime(CurrentBar).ToString("HHmmss"));
                MyPrint(defaultErrorType, "OnBarUpdate, TServer response= <<<  " + tServerSignal + "  >>> ");
                //MyPrint(defaultErrorType, "Time Server signal=" + tServerSignal);

                SetTServerSignals(tServerSignal[0]);
                SetExitSignals(tServerSignal[0]);

                WriteMarketView(currMarketView);
                WriteExitView(currExitView);
                MyPrint(defaultErrorType, DateTime.Now + " Current T-Server View = {{{{{ " + currMarketView.ToString() + " }}}}} ");
            }
        }
    }
}