#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
using System.Diagnostics;
using System.IO;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class AGG6BarDailyMonthlyDrawdownLive3001 : Strategy
    {
        private string path;
        private string pathErr;
        private StreamWriter sw = null; // a variable for the StreamWriter that will be used 
        private StreamWriter swErr = null;

        private int Fast;
        private int Slow;

        private Order entryOrder = null; // This variable holds an object representing our entry order
        private Order stopOrder = null; // This variable holds an object representing our stop loss order
        private Order targetOrder = null; // This variable holds an object representing our profit target order
        private int sumFilled = 0; // This variable tracks the quantities of each execution making up the entry order

        // these constants affects how the drawdown policy is being enforced, typical settings are 7-5-2 and 6-4-2
        private static int MaxConsecutiveLossesUpper = 6;
        private static int MaxConsecutiveLosses = 4;
        private static int MinConsecutiveWins = 2;

        private static double CommissionRate = 5.48;

        //below are Monthly drawdown (Profit chasing and stop loss) strategy constants that could be experimented
        private static double ProfitChasingTarget = 0.75; // % monthly gain profit target
        private static double MaxPercentAllowableDrawdown = 0.5; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
        private static double ProfitChasingAllowableDrawdown = 0.1; // allowable max % drawdown if profit chasing target is achieved before trading halt for the month

        private static readonly int lotSize = 2;
        private static double InitStartingCapital = 10000; // assume starting capital is $10,000

        private bool haltTrading = false;
        private bool realTimeMode = false;  //indicate that the system is now in real time mode - live trading

        //below are variables accounting for each trading day for the month
        private double startingCapital = InitStartingCapital; // set before trading starts for the month
        private double prevCapital = InitStartingCapital; // set to  startingCapital before the month
        private double currentCapital = InitStartingCapital; // set to  startingCapital before the month
        private bool monthlyProfitChasingFlag = false; // set to false before the month

        private int maxConsecutiveDailyLosses = MaxConsecutiveLosses;
        private int consecutiveDailyLosses = 0;
        private int consecutiveDailyWins = 0;

        private string svrSignal = "1";

        private static readonly int profitChasing = 20 * 4; // the target where HandleProfitChasing kicks in
        private static readonly int profitTarget = profitChasing * 10; // for automatic profits taking, HandleProfitChasing will take care of profit taking once profit > profitChasing
        private static readonly int softDeck = 10 * 4; // number of stops for soft stop loss
        private static readonly int hardDeck = 20 * 4; //hard deck for auto stop loss
        private static readonly int portNumber = 3001;
        private double closedPrice = 0.0;
        // *** NOTE ***: NEED TO MODIFY the HH and MM of the endSessionTime to user needs, always minus bufferUntilEOD minutes to allow for buffer checking of end of session time, e.g. 23HH 59-10MM
        private static int bufferUntilEOD = 10;  // number of minutes before end of session
        private DateTime regularEndSessionTime = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 15, (15 - bufferUntilEOD), 00);
        private DateTime fridayEndSessionTime = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 15, (15 - bufferUntilEOD), 00);
        private bool endSession = false;

        // global flags
        private bool profitChasingFlag = false;
        private bool stopLossEncountered = false;

        private Socket sender = null;
        private byte[] bytes = new byte[1024];
        int lineNo = 0;

        enum Position
        {
            posFlat,
            posShort,
            posLong
        };
        Position currPos = Position.posFlat;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Implements live trading for the daily drawdown control and monthly profit chasing/stop loss strategy";
                Name = "AGG6BarDailyMonthlyDrawdownLive3001";
                //Calculate = Calculate.OnEachTick; //Must be on each tick, otherwise won't check time in real time.
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
                StopTargetHandling = StopTargetHandling.ByStrategyPosition;
                BarsRequiredToTrade = 0;
                Fast = 10;
                Slow = 25;

                //// Connect to DLNN Server  
                //try
                //{
                //    // Do not attempt connection if already connected
                //    if (sender != null)
                //        return;

                //    // Establish the remote endpoint for the socket.  
                //    // connecting server on port 3333  
                //    IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
                //    // IPAddress ipAddress = ipHostInfo.AddressList[1]; depending on the Wifi set up, this index may change accordingly
                //    IPAddress ipAddress = ipHostInfo.AddressList[4];
                //    IPEndPoint remoteEP = new IPEndPoint(ipAddress, portNumber);

                //    MyPrint("ipHostInfo=" + ipHostInfo.HostName.ToString() + " ipAddress=" + ipAddress.ToString());

                //    // Create a TCP/IP  socket.  
                //    sender = new Socket(ipAddress.AddressFamily,
                //        SocketType.Stream, ProtocolType.Tcp);

                //    // Connect the socket to the remote endpoint. Catch any errors.  
                //    try
                //    {
                //        sender.Connect(remoteEP);

                //        MyPrint(" ************ Socket connected to : " +
                //            sender.RemoteEndPoint.ToString() + "*************");

                //        // TODO: Release the socket.  
                //        //sender.Shutdown(SocketShutdown.Both);
                //        //sender.Close();

                //    }
                //    catch (ArgumentNullException ane)
                //    {
                //        MyPrint("ArgumentNullException : " + ane.ToString());
                //    }
                //    catch (SocketException se)
                //    {
                //        MyPrint("SocketException : " + se.ToString());
                //    }
                //    catch (Exception e)
                //    {
                //        MyPrint("Unexpected exception : " + e.ToString());
                //    }

                //}
                //catch (Exception e)
                //{
                //    MyPrint(e.ToString());
                //}
            }
            else if (State == State.Configure)
            {
                /* Add a secondary bar series.
Very Important: This secondary bar series needs to be smaller than the primary bar series.

Note: The primary bar series is whatever you choose for the strategy at startup.
In our case it is a 2000 ticks bar. */
                AddDataSeries(Data.BarsPeriodType.Tick, 1);

                //            // Add two EMA indicators to be plotted on the primary bar series
                //            AddChartIndicator(EMA(Fast));
                //            AddChartIndicator(EMA(Slow));

                //            /* Adjust the color of the EMA plots.
                //For more information on this please see this tip: http://www.ninjatrader-support.com/vb/showthread.php?t=3228 */
                //            EMA(Fast).Plots[0].Brush = Brushes.Blue;
                //            EMA(Slow).Plots[0].Brush = Brushes.Green;


                // set static profit target and stop loss
                SetProfitTarget(CalculationMode.Ticks, profitTarget);
                SetStopLoss(CalculationMode.Ticks, hardDeck);
            }
            else if (State == State.Realtime)
            {
                // one time only, as we transition from historical
                // convert any old historical order object references
                // to the new live order submitted to the real-time account
                if (entryOrder != null)
                    entryOrder = GetRealtimeOrder(entryOrder);
                if (stopOrder != null)
                    stopOrder = GetRealtimeOrder(stopOrder);
                if (targetOrder != null)
                    targetOrder = GetRealtimeOrder(targetOrder);

                realTimeMode = true;
            }
            else if (State == State.DataLoaded)
            {

                // Connect to DLNN Server  
                try
                {
                    // Do not attempt connection if already connected
                    if (sender != null)
                        return;

                    // Establish the remote endpoint for the socket.  
                    // connecting server on portNumber  
                    IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
                    IPAddress ipAddress = ipHostInfo.AddressList[1]; // depending on the Wifi set up, this index may change accordingly
                    // IPAddress ipAddress = ipHostInfo.AddressList[4];
                    IPEndPoint remoteEP = new IPEndPoint(ipAddress, portNumber);

                    MyPrint("ipHostInfo=" + ipHostInfo.HostName.ToString() + " ipAddress=" + ipAddress.ToString());

                    // Create a TCP/IP  socket.  
                    sender = new Socket(ipAddress.AddressFamily,
                        SocketType.Stream, ProtocolType.Tcp);

                    // Connect the socket to the remote endpoint. Catch any errors.  
                    try
                    {
                        sender.Connect(remoteEP);

                        MyPrint(" ************ Socket connected to : " +
                            sender.RemoteEndPoint.ToString() + "*************");

                        // TODO: Release the socket.  
                        //sender.Shutdown(SocketShutdown.Both);
                        //sender.Close();

                    }
                    catch (ArgumentNullException ane)
                    {
                        MyErrPrint("Socket Connect Error: ArgumentNullException : " + ane.ToString());
                    }
                    catch (SocketException se)
                    {
                        MyErrPrint("Socket Connect Error: SocketException : " + se.ToString());
                    }
                    catch (Exception e)
                    {
                        MyErrPrint("Socket Connect Error: Unexpected exception : " + e.ToString());
                    }
                }
                catch (Exception e)
                {
                    MyErrPrint(e.ToString());
                }
            }
            // Necessary to call in order to clean up resources used by the StreamWriter object
            else if (State == State.Terminated)
            {
                if (sw != null)
                {
                    sw.Close();
                    sw.Dispose();
                    sw = null;
                    swErr.Close();
                    swErr.Dispose();
                    swErr = null;
                }
            }
        }

        protected override void OnAccountItemUpdate(Cbi.Account account, Cbi.AccountItem accountItem, double value)
        {

        }

        protected override void OnConnectionStatusUpdate(ConnectionStatusEventArgs connectionStatusUpdate)
        {

        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            /* We advise monitoring OnExecution to trigger submission of stop/target orders instead of OnOrderUpdate() since OnExecution() is called after OnOrderUpdate()
            which ensures your strategy has received the execution which is used for internal signal tracking. */
            //if (execution.Order != null && (execution.Order.OrderState == OrderState.Filled || execution.Order.OrderState == OrderState.PartFilled))
            //{
            //    if (execution.Order.Name == "Stop loss")
            //    {
            //        MyPrint(execution.Time.ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " @@@@@ L O S E R @@@@@@ OnExecutionUpdate::Stop loss" + " OrderState=" + execution.Order.OrderState.ToString() + " OPEN=" + closedPrice.ToString() + " CLOSE=" + execution.Order.AverageFillPrice.ToString());
            //        MyPrint("---------------------------------------------------------------------------------");

            //        //reset global flags
            //        currPos = Position.posFlat;
            //        profitChasingFlag = false;
            //        stopLossEncountered = true;
            //    }
            //}
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            // Handle entry orders here. The entryOrder object allows us to identify that the order that is calling the OnOrderUpdate() method is the entry order.
            // Assign entryOrder in OnOrderUpdate() to ensure the assignment occurs when expected.
            // This is more reliable than assigning Order objects in OnBarUpdate, as the assignment is not gauranteed to be complete if it is referenced immediately after submitting
            if (order.Name == "Long" || order.Name == "Short")
            {
                entryOrder = order;

                if (order.Filled == 1)
                {
                    closedPrice = order.AverageFillPrice;
                    if (order.Name == "Long")
                        currPos = Position.posLong;
                    if (order.Name == "Short")
                        currPos = Position.posShort;

                    MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " #######Order filled, closedPrice=" + closedPrice + " order name=" + order.Name + " currPos=" + currPos.ToString());
                }

                // Reset the entryOrder object to null if order was cancelled without any fill
                if (order.OrderState == OrderState.Cancelled && order.Filled == 0)
                {
                    entryOrder = null;
                    sumFilled = 0;
                }
            }
        }


        private void MyErrPrint(string buf)
        {
            //Set this scripts MyPrint() calls to the first output tab
            PrintTo = PrintTo.OutputTab1;

            if (swErr == null)
            {
                pathErr = NinjaTrader.Core.Globals.UserDataDir + portNumber.ToString() + "-" + Time[0].ToString("yyyyMMdd") + ".err"; // Define the Path to our err file
                swErr = File.AppendText(pathErr);  // Open the path for err file writing
            }

            swErr.WriteLine(buf); // Append a new line to the err file
            Print(buf);
        }

        private void MyPrint(string buf)
        {
            //Set this scripts MyPrint() calls to the second output tab
            PrintTo = PrintTo.OutputTab2;

            if (sw == null)
            {
                //Create log file in the portNumber-yyyyMMdd.log format
                path = NinjaTrader.Core.Globals.UserDataDir + portNumber.ToString() + "-" + Time[0].ToString("yyyyMMdd") + ".log"; // Define the Path to our log file
                sw = File.AppendText(path);  // Open the path for log file writing
            }

            sw.WriteLine(buf); // Append a new line to the log file
            Print(buf);
        }

        private void ResetWinLossState()
        {
            maxConsecutiveDailyLosses = MaxConsecutiveLosses;
            consecutiveDailyLosses = 0;
            consecutiveDailyWins = 0;
        }

        // increment of daily win will increase max consecutive daily losses as long as it does not exceed the upper limit, it will also reset consecutive daily losses back to zero
        private void IncrementDailyWin()
        {
            consecutiveDailyWins++;
            consecutiveDailyLosses = 0;

            if (consecutiveDailyWins >= MinConsecutiveWins)
            {
                if (maxConsecutiveDailyLosses < MaxConsecutiveLossesUpper)
                {
                    maxConsecutiveDailyLosses++;
                }
                consecutiveDailyWins = 0;
            }
        }

        private void IncrementDailyLoss()
        {
            consecutiveDailyWins = 0;
            consecutiveDailyLosses++;
        }

        // WARNING!!!! Market position is not order position
        protected override void OnPositionUpdate(Cbi.Position position, double averagePrice,
            int quantity, Cbi.MarketPosition marketPosition)
        {
            if (position.MarketPosition == MarketPosition.Flat)
            {
                PrintDailyProfitAndLoss();
                ResetGlobalFlags(false);
            }
            if (position.MarketPosition == MarketPosition.Long)
            {
            }
            if (position.MarketPosition == MarketPosition.Short)
            {
            }
        }

        private string ExtractResponse(string repStr)
        {
            int index = 1;
            foreach (char ch in repStr)
            {
                if (ch != ',')
                    index++;
                else
                    break;
            }

            return repStr.Substring(index, 1);
        }

        private bool PosFlat()
        {
            return (currPos == Position.posFlat);
        }

        private bool PosShort()
        {
            return (currPos == Position.posShort);
        }

        private bool PosLong()
        {
            return (currPos == Position.posLong);
        }

        private void AiShort()
        {
            EnterShort(lotSize, "Short");
            MyPrint("Server Signal=" + svrSignal + " Short");
        }

        private void AiLong()
        {
            EnterLong(lotSize, "Long");
            MyPrint("Server Signal=" + svrSignal + " Long");
        }

        private void ResetGlobalFlags(bool stopLoss)
        {
            currPos = Position.posFlat;
            profitChasingFlag = false;
            stopLossEncountered = stopLoss;
        }


        private void AiFlat()
        {
            MyPrint("AiFlat: currPos = " + currPos.ToString());

            if (PosLong())
            {
                ExitLong("ExitLong", "Long");
                MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " AiFlat::ExitLong");
                MyPrint("---------------------------------------------------------------------------------");
            }
            if (PosShort())
            {
                ExitShort("ExitShort", "Short");
                MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " AiFlat::ExitShort");
                MyPrint("---------------------------------------------------------------------------------");
            }
            //PrintDailyProfitAndLoss();

            //reset global flags
            ResetGlobalFlags(false);
        }

        private void StartTradePosition(string signal)
        {
            //MyPrint("StartTradePosition");
            switch (signal[0])
            {
                case '0':
                    // sell
                    AiShort();
                    break;
                case '2':
                    // buy
                    AiLong();
                    break;
                default:
                    // do nothing if signal is 1 for flat position
                    break;
            }
        }

        private void ExecuteAITrade(string signal)
        {
            if (haltTrading)
                return;

            // don't execute trade if consecutive losses greater than allowable limits
            if (consecutiveDailyLosses >= maxConsecutiveDailyLosses)
            {
                MyPrint("consecutiveDailyLosses >= maxConsecutiveDailyLosses, Skipping StartTradePosition");
                return;
            }

            // Set monthlyProfitChasingFlag, once monthlyProfitChasingFlag sets to true, it will stay true until end of the month
            if (!monthlyProfitChasingFlag)
            {
                if (currentCapital > (startingCapital * (1 + ProfitChasingTarget)))
                {
                    monthlyProfitChasingFlag = true;
                    MyPrint("$$$$$$$$$$$$$ Monthly profit target met, Monthly Profit Chasing and Stop Loss begins! $$$$$$$$$$$$$");
                }
            }

            // Don't trade if monthly profit chasing and stop loss strategy decided not to trade for the rest of the month
            if (monthlyProfitChasingFlag)
            {
                // trading halt if suffers more than ProfitChasingAllowableDrawdown losses from prevCapital
                if (currentCapital < (prevCapital * (1 - ProfitChasingAllowableDrawdown)))
                {
                    MyPrint("$$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartTradePosition $$$$$$$!!!!!!!!");
                    haltTrading = true;
                    return;
                }
            }
            else
            {
                // trading halt if suffers more than MaxPercentAllowableDrawdown losses
                if (currentCapital < (startingCapital * (1 - MaxPercentAllowableDrawdown)))
                {
                    MyPrint("!!!!!!!!!!!! Monthly profit target NOT met, stop loss enforced, Skipping StartTradePosition !!!!!!!!!!!!");
                    haltTrading = true;
                    return;
                }
            }

            //MyPrint("ExecuteAITrade");
            if (PosFlat())
            {
                StartTradePosition(signal);
                return;
            }
        }

        private void HandleSoftDeck(string signal)
        {
            if (PosFlat())
            {
                // this is not possible
                Debug.Assert(!PosFlat(), "ASSERT: Position is flat while HandleSoftDeck");
                return;
            }

            if (PosLong())
            {
                if (signal[0] != '2')
                {
                    //MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleSoftDeck:: signal= " + signal.ToString() + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " soft deck=" + (softDeck * TickSize).ToString() + " @@@@@ L O S E R @@@@@@ loss= " + (Close[0]-closedPrice).ToString());
                    MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleSoftDeck:: signal= " + signal.ToString() + " OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0] + " soft deck=" + (softDeck * TickSize).ToString() + " @@@@@ L O S E R @@@@@@ loss= " + ((Close[0] - closedPrice) * 50 - CommissionRate).ToString());
                    AiFlat();

                    IncrementDailyLoss();

                    // keeping records for monthly profit chasing and stop loss strategy
                    currentCapital += ((Close[0] - closedPrice) * 50 - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (currentCapital < prevCapital))
                    {
                        MyPrint("currentCapital=" + currentCapital.ToString() + " prevCapital=" + prevCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartTradePosition $$$$$$$!!!!!!!!");
                        haltTrading = true;
                    }
                }
                return;
            }

            if (PosShort())
            {
                if (signal[0] != '0')
                {
                    //MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleSoftDeck:: signal= " + signal.ToString() + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " soft deck=" + (softDeck * TickSize).ToString() + " @@@@@ L O S E R @@@@@@ loss= " + (closedPrice- Close[0]).ToString());
                    MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleSoftDeck:: signal= " + signal.ToString() + " OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0] + " soft deck=" + (softDeck * TickSize).ToString() + " @@@@@ L O S E R @@@@@@ loss= " + ((closedPrice - Close[0]) * 50 - CommissionRate).ToString());
                    AiFlat();

                    IncrementDailyLoss();

                    // keeping records for monthly profit chasing and stop loss strategy
                    currentCapital += ((closedPrice - Close[0]) * 50 - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (currentCapital < prevCapital))
                    {
                        MyPrint("currentCapital=" + currentCapital.ToString() + " prevCapital=" + prevCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartTradePosition $$$$$$$!!!!!!!!");
                        haltTrading = true;
                    }
                }
                return;
            }
        }

        private bool ViolateSoftDeck()
        {
            if (PosLong())
            {
                return (Bars.GetClose(CurrentBar) <= (closedPrice - softDeck * TickSize));
            }
            if (PosShort())
            {
                return (Bars.GetClose(CurrentBar) >= (closedPrice + softDeck * TickSize));
            }
            return false;
        }

        private void HandleHardDeck()
        {
            if (PosFlat())
            {
                // this is not possible
                Debug.Assert(!PosFlat(), "ASSERT: Position is flat while HandleHardDeck");
                return;
            }

            if (PosLong())
            {
                MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleHardDeck:: " + " OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0] + " @@@@@ L O S E R @@@@@@ loss= " + ((Close[0] - closedPrice) * 50 - CommissionRate).ToString());
                AiFlat();

                IncrementDailyLoss();

                // keeping records for monthly profit chasing and stop loss strategy
                currentCapital += ((Close[0] - closedPrice) * 50 - CommissionRate);

                // stop trading if monthly profit is met and trading going negative
                if (monthlyProfitChasingFlag && (currentCapital < prevCapital))
                {
                    MyPrint("currentCapital=" + currentCapital.ToString() + " prevCapital=" + prevCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartTradePosition $$$$$$$!!!!!!!!");
                    haltTrading = true;
                }
            }

            if (PosShort())
            {
                MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleHardDeck:: " + " OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0] + " @@@@@ L O S E R @@@@@@ loss= " + ((closedPrice - Close[0]) * 50 - CommissionRate).ToString());
                AiFlat();

                IncrementDailyLoss();

                // keeping records for monthly profit chasing and stop loss strategy
                currentCapital += ((closedPrice - Close[0]) * 50 - CommissionRate);

                // stop trading if monthly profit is met and trading going negative
                if (monthlyProfitChasingFlag && (currentCapital < prevCapital))
                {
                    MyPrint("currentCapital=" + currentCapital.ToString() + " prevCapital=" + prevCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartTradePosition $$$$$$$!!!!!!!!");
                    haltTrading = true;
                }
            }
        }

        private bool ViolateHardDeck()
        {
            if (PosLong())
            {
                return (Close[0] <= (closedPrice - hardDeck * TickSize));
            }
            if (PosShort())
            {
                return (Close[0] >= (closedPrice + hardDeck * TickSize));
            }
            return false;
        }

        private void HandleProfitChasing(string signal)
        {
            if (PosFlat())
            {
                // this is not possible
                Debug.Assert(!PosFlat(), "ASSERT: Position is flat while HandleProfitChasing");
                return;
            }
            // if market trend go against profit positions and server signal against current position, then flatten position and take profits
            if (PosLong())
            {
                if (Bars.GetClose(CurrentBar) < Bars.GetClose(CurrentBar - 1) && signal[0] == '0')
                {
                    //MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleProfitChasing::" + " currPos=" + currPos.ToString() + " closedPrice=" + closedPrice.ToString() + " Close[0]=" + Close[0].ToString() + " closedPrice + profitChasing=" + (closedPrice + profitChasing * TickSize).ToString() + " >>>>>> W I N N E R >>>>>> Profits= " + (Close[0] - closedPrice).ToString());
                    MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleProfitChasing::" + " currPos=" + currPos.ToString() + " OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0].ToString() + " >>>>>> W I N N E R >>>>>> Profits= " + ((Close[0] - closedPrice) * 50 - CommissionRate).ToString());
                    AiFlat();

                    IncrementDailyWin();

                    // keeping records for monthly profit chasing and stop loss strategy
                    currentCapital += ((Close[0] - closedPrice) * 50 - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (currentCapital < prevCapital))
                    {
                        MyPrint("currentCapital=" + currentCapital.ToString() + " prevCapital=" + prevCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartTradePosition $$$$$$$!!!!!!!!");
                        haltTrading = true;
                    }
                }
            }
            if (PosShort())
            {
                if (Bars.GetClose(CurrentBar) > Bars.GetClose(CurrentBar - 1) && signal[0] == '2')
                {
                    MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleProfitChasing::" + " currPos=" + currPos.ToString() + " OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0].ToString() + " >>>>>> W I N N E R >>>>>> Profits= " + ((closedPrice - Close[0]) * 50 - CommissionRate).ToString());
                    AiFlat();

                    IncrementDailyWin();

                    // keeping records for monthly profit chasing and stop loss strategy
                    currentCapital += ((closedPrice - Close[0]) * 50 - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (currentCapital < prevCapital))
                    {
                        MyPrint("currentCapital=" + currentCapital.ToString() + " prevCapital=" + prevCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartTradePosition $$$$$$$!!!!!!!!");
                        haltTrading = true;
                    }
                }
            }
        }

        private bool TouchedProfitChasing()
        {
            profitChasingFlag = false;

            if (PosLong())
            {
                //if (Close[0] >= (closedPrice + profitChasing * TickSize))
                if (Bars.GetClose(CurrentBar) >= (closedPrice + profitChasing * TickSize))
                {
                    MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " TouchedProfitChasing");
                    profitChasingFlag = true;
                    return profitChasingFlag;
                }
            }
            if (PosShort())
            {
                //if (Close[0] <= (closedPrice - profitChasing * TickSize))
                if (Bars.GetClose(CurrentBar) <= (closedPrice - profitChasing * TickSize))
                {
                    MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " TouchedProfitChasing");
                    profitChasingFlag = true;
                    return profitChasingFlag;
                }
            }

            return profitChasingFlag;
        }

        private void CloseCurrentPositions()
        {
            // EOD close current position(s)
            if (PosLong())
            {
                MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleEOD:: " + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " Close[0]=" + Close[0].ToString() + " P/L= " + ((Close[0] - closedPrice) * 50 - CommissionRate).ToString());

                AiFlat();

                // keeping records for monthly profit chasing and stop loss strategy
                currentCapital += ((Close[0] - closedPrice) * 50 - CommissionRate);

                // stop trading if monthly profit is met and trading going negative
                if (monthlyProfitChasingFlag && (currentCapital < prevCapital))
                {
                    MyPrint("currentCapital=" + currentCapital.ToString() + " prevCapital=" + prevCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartTradePosition $$$$$$$!!!!!!!!");
                    haltTrading = true;
                }
                return;
            }

            if (PosShort())
            {
                MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleEOD:: " + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " Close[0]=" + Close[0].ToString() + " P/L= " + ((closedPrice - Close[0]) * 50 - CommissionRate).ToString());

                AiFlat();

                // keeping records for monthly profit chasing and stop loss strategy
                currentCapital += ((closedPrice - Close[0]) * 50 - CommissionRate);

                // stop trading if monthly profit is met and trading going negative
                if (monthlyProfitChasingFlag && (currentCapital < prevCapital))
                {
                    MyPrint("currentCapital=" + currentCapital.ToString() + " prevCapital=" + prevCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartTradePosition $$$$$$$!!!!!!!!");
                    haltTrading = true;
                }
                return;
            }
        }

        private void ResetServer()
        {
            //CloseCurrentPositions();

            string resetString = "-1";
            byte[] resetMsg = Encoding.UTF8.GetBytes(resetString);

            // Send reset string of "-1" to the server  
            int resetSent = sender.Send(resetMsg);

            //reset global flags
            ResetGlobalFlags(false);
            lineNo = 0;
        }

        private void PrintDailyProfitAndLoss()
        {   // MyPrint out the net profit of all trades
            MyPrint("$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
            MyPrint("Cumulative net profit is: " + SystemPerformance.AllTrades.TradesPerformance.NetProfit);
            MyPrint("$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
        }

        // Need to Handle end of session on tick because to avoid closing position past current day
        private void HandleEndOfSession()
        {
            DateTime endSessionTime;

            prevCapital = currentCapital;

            // pick the correct End session time
            if (Time[0].DayOfWeek == DayOfWeek.Friday)
            {
                endSessionTime = fridayEndSessionTime;
            }
            else
            {
                endSessionTime = regularEndSessionTime;
            }

            if (!endSession && Time[0].Hour == endSessionTime.Hour)
            {
                if (Time[0].Minute > endSessionTime.Minute)
                {
                    MyPrint("HandleEndOfSession for Time= " + endSessionTime.ToString("HH:mm"));
                    MyPrint("Current Time[0]= " + Time[0].ToString("HH:mm"));

                    CloseCurrentPositions();

                    ResetServer();
                    endSession = true;

                    ResetWinLossState();
                }
            }
        }

        protected override void OnBarUpdate()
        {
            /* When working with multiple bar series objects it is important to understand the sequential order in which the
            OnBarUpdate() method is triggered. The bars will always run with the primary first followed by the secondary and
            so on.

            Important: Primary bars will always execute before the secondary bar series.
            If a bar is timestamped as 12:00PM on the 5min bar series, the call order between the equally timestamped 12:00PM
            bar on the 1min bar series is like this:
                12:00PM 5min
                12:00PM 1min
                12:01PM 1min
                12:02PM 1min
                12:03PM 1min
                12:04PM 1min
                12:05PM 5min
                12:05PM 1min

            When the OnBarUpdate() is called from the primary bar series (2000 ticks series in this example), do the following */
            if (BarsInProgress == 0)
            {
                //if (CurrentBar < BarsRequiredToTrade)
                //    return;

                // for live trading, don't start feeding bars to the server until in real time mode
                if (!realTimeMode)
                    return;

                //ignore all bars that come after end of session, until next day
                if (endSession)
                {
                    // if new day, then reset endSession
                    if (Bars.GetTime(CurrentBar).Date > Bars.GetTime(CurrentBar - 1).Date)
                    {
                        endSession = false;
                        lineNo = 0;
                    }
                    else
                    {
                        return;
                    }
                }

                // For live trading, don't modify lineNo when Stop Loss was encountered
                // prior Stop-Loss observed, construct the lineNo with special code before sending msg to the server - so that the server will flatten the position
                //if (stopLossEncountered)
                //{
                //    lineNo += 10000;
                //}

                // HandleEndOfSession() will handle End of day (e.g. 2359pm), End of session (e.g. 1515pm) and this will handle occasional missing historical data cases
                //if (CurrentBar != 0 && (Bars.GetTime(CurrentBar - 1).TimeOfDay > Bars.GetTime(CurrentBar).TimeOfDay))
                //{
                //    MyPrint("!!!!!!!!!!! Missing data detected !!!!!!!!!!!:: <<CurrentBar - 1>> :" + Bars.GetTime(CurrentBar - 1).ToString("yyyy-MM-ddTHH:mm:ss") + "   <<CurrentBar>> :" + Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss"));

                //    // close current positions, reset the server and skip to next bar
                //    CloseCurrentPositions();
                //    ResetServer();
                //    return;
                //}

                string bufString;

                if (Bars.IsFirstBarOfSession)
                {
                    // construct the string buffer to be sent to DLNN
                    bufString = lineNo.ToString() + ',' +
                        "000000" + ',' + Bars.GetTime(CurrentBar).ToString("HHmmss") + ',' +
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
                }
                else
                {
                    // construct the string buffer to be sent to DLNN
                    bufString = lineNo.ToString() + ',' +
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
                }
                //MyPrint("CurrentBar = " + CurrentBar + ": " + "bufString = " + bufString);

                byte[] msg = Encoding.UTF8.GetBytes(bufString);


                int bytesSent;
                int bytesRec;

                try
                {
                    // Send the data through the socket.  
                    bytesSent = sender.Send(msg);

                    // Receive the response from the remote device.  
                    bytesRec = sender.Receive(bytes);
                }
                catch (SocketException ex)
                {
                    MyErrPrint("Socket exception::" + ex.Message + "" + ex.ToString());
                }

                // for Live Trading, don't reset server and change lineNo
                // prior Stop-Loss observed, hence ignore the returned signal from server and move on to the next bar, reset lineNo to next counter and reset stopLossEncountered flag
                //if (stopLossEncountered)
                //{
                //    lineNo -= 10000;
                //    lineNo++;
                //    stopLossEncountered = false;

                //    //svrSignal = ExtractResponse(System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length));
                //    svrSignal = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length).Split(',')[1];
                //    MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " Ignore Post STOP-LOSS Server response= <" + svrSignal + "> Current Bar: Open=" + Bars.GetOpen(CurrentBar) + " Close=" + Bars.GetClose(CurrentBar) + " High=" + Bars.GetHigh(CurrentBar) + " Low=" + Bars.GetLow(CurrentBar));

                //    return;
                //}

                //svrSignal = ExtractResponse(System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length));
                svrSignal = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length).Split(',')[1];
                MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " Server response= <" + svrSignal + "> Current Bar: Open=" + Bars.GetOpen(CurrentBar) + " Close=" + Bars.GetClose(CurrentBar) + " High=" + Bars.GetHigh(CurrentBar) + " Low=" + Bars.GetLow(CurrentBar));
                //MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " Server response= <" + svrSignal + ">");

                lineNo++;

                // Start processing signal after 8th signal and beyond, otherwise ignore
                if (lineNo >= 8)
                {
                    ExecuteAITrade(svrSignal);

                    // if position is flat, no need to do anything
                    if (currPos == Position.posFlat)
                        return;

                    // handle stop loss or profit chasing if there is existing position and order action is either SellShort or Buy
                    if (entryOrder != null && (entryOrder.OrderAction == OrderAction.Buy || entryOrder.OrderAction == OrderAction.SellShort) && (entryOrder.OrderState == OrderState.Filled || entryOrder.OrderState == OrderState.PartFilled))
                    {
                        // if Close[0] violates soft deck, if YES handle stop loss accordingly
                        if (ViolateSoftDeck())
                        {
                            HandleSoftDeck(svrSignal);
                        }

                        // if profitChasingFlag is TRUE or TouchedProfitChasing then handle profit chasing
                        if ((profitChasingFlag || TouchedProfitChasing()))
                        {
                            HandleProfitChasing(svrSignal);
                        }
                    }
                }
            }
            // When the OnBarUpdate() is called from the secondary bar series, in our case for each tick, handle End of session
            else
            {

                // Need to Handle end of session on tick because to avoid closing position past current day
                HandleEndOfSession();

                // HandleEndOfSession would close all positions
                if (!endSession && ViolateHardDeck())
                {
                    HandleHardDeck();

                    //reset global flags
                    ResetGlobalFlags(true);
                }
                return;
            }
        }
    }
}
