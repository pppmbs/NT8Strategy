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
        // log, error, current capital and vix  files
        private string pathLog;
        private string pathErr;
        private string pathCC;
        private string pathVIX;
        private StreamWriter swLog = null; // runtime log file 
        private StreamWriter swErr = null; // error file
        private StreamWriter swCC = null;  // Store current capital for each strategy 
        private StreamWriter swVIX = null;  // Store 10 days Moving average VIX

        private Order entryOrder = null; // This variable holds an object representing our entry order
        private Order stopOrder = null; // This variable holds an object representing our stop loss order
        private Order targetOrder = null; // This variable holds an object representing our profit target order

        // following two variables are placeholders, not currently being used
        private int sumFilled = 0; // This variable tracks the quantities of each execution making up the entry order
        private bool orderPartialFilled = false;


        /* **********************************************************************************************************
         * Following settings need to be set before run
         * **********************************************************************************************************
         */
        // these constants affects how the drawdown policy is being enforced,  
        // current optimal low vix settings 7-5-2 / 60-30-10, high vix >= 40 settings 4-2-2 / 75-10-5
        private static double HighVixTreshold = 40;

        //below are Daily drawdown (counting wins and losses) strategy settings
        // Low VIX daily drawdown control settings
        private static int LVmaxConsecutiveLossesUpper = 7;  // upper limit allowable daily losses
        private static int LVmaxConsecutiveLosses = 5;      // max allowable daily losses if no win
        private static int LVminConsecutiveWins = 2;       // min wins to increment max allowable daily losses 
        // High VIX daily drawdown control settings
        private static int HVmaxConsecutiveLossesUpper = 4; // upper limit allowable daily losses
        private static int HVmaxConsecutiveLosses = 2;     // max allowable daily losses if no win
        private static int HVminConsecutiveWins = 2;      // min wins to increment max allowable daily losses

        //below are Monthly drawdown (Profit chasing and stop loss) strategy settings
        //Low VIX monthly drawdown control settings
        private static double LVprofitChasingTarget = 0.6; // % monthly gain profit target
        private static double LVmaxPercentAllowableDrawdown = 0.3; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
        private static double LVprofitChasingAllowableDrawdown = 0.1; // allowable max % drawdown if profit chasing target is achieved before trading halt for the month
        // High VIX monthly drawdown control settings
        private static double HVprofitChasingTarget = 0.75; // % monthly gain profit target
        private static double HVmaxPercentAllowableDrawdown = 0.1; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
        private static double HVprofitChasingAllowableDrawdown = 0.05; // allowable max % drawdown if profit chasing target is achieved before trading halt for the month

        // initial trading capital and trading lot size
        private static readonly int LotSize = 2;

        // IMPORTANT: initial starting capital is set to $10,000 for monthly drawdown control strategy accounting purpose, 
        //            the monthly drawdown comparison is based on %percentage% of $10,000
        //            even though capital to lot ratio can be set to $25,000 per lot
        private static double InitStartingCapital = 10000; 

        /* **********************************************************************************************************
         * Commission rate needs to be set to the current commission rate
         * **********************************************************************************************************
         */
        private static double CommissionRate = 5.48;
        /*
         * **********************************************************************************************************
         */

        // these variables affects how the daily drawdown policy is being enforced
        private int maxConsecutiveLossesUpper = LVmaxConsecutiveLossesUpper;
        private int maxConsecutiveLosses = LVmaxConsecutiveLossesUpper;
        private int minConsecutiveWins = LVmaxConsecutiveLossesUpper;

        // these variables affects how the monthly drawdown policy is being enforced 
        private double profitChasingTarget = LVprofitChasingTarget; // % monthly gain profit target
        private double maxPercentAllowableDrawdown = LVmaxPercentAllowableDrawdown; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
        private double profitChasingAllowableDrawdown = LVprofitChasingAllowableDrawdown; // allowable max % drawdown if profit chasing target is achieved before trading halt for the month

        private double currentCapital = InitStartingCapital; // set to  startingCapital before the day

        // below are variables accounting for each trading day, tracking monthly drawdown control strategy
        // they are to be initialized when State == State.Realtime during start up
        private double yesterdayCapital = InitStartingCapital; // set to  InitStartingCapital before the run, it will get initialized when State == State.Realtime
        private bool monthlyProfitChasingFlag = false; // set to false before the month

        private int maxConsecutiveDailyLosses = 0;
        private int consecutiveDailyLosses = 0;
        private int consecutiveDailyWins = 0;

        private string svrSignal = "1";

        /* **********************************************************************************************************
         * Following settings need to be set once
         * **********************************************************************************************************
         */
        private static readonly int profitChasing = 20 * 4; // the target where HandleProfitChasing kicks in
        private static readonly int profitTarget = profitChasing * 10; // for automatic profits taking, HandleProfitChasing will take care of profit taking once profit > profitChasing
        private static readonly int softDeck = 10 * 4; // number of stops for soft stop loss
        private static readonly int hardDeck = 20 * 4; //hard deck for auto stop loss
        private static readonly int portNumber = 3001;
        /*
         * **********************************************************************************************************
         */
        private double closedPrice = 0.0;
        // *** NOTE ***: NEED TO MODIFY the HH and MM of the endSessionTime to user needs, always minus bufferUntilEOD minutes to allow for buffer checking of end of session time, e.g. 23HH 59-10MM
        private static int bufferUntilEOD = 10;  // number of minutes before end of session
        private DateTime regularEndSessionTime = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 15, (15 - bufferUntilEOD), 00);
        private DateTime fridayEndSessionTime = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 15, (15 - bufferUntilEOD), 00);
        private bool endSession = false;

        // global flags
        private bool profitChasingFlag = false;
        private bool stopLossEncountered = false;
        private bool attemptToFlattenPos = false;
        private bool haltTrading = false;


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

        enum ErrorType
        {
            warning,
            fatal
        };

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Implements live trading for the daily drawdown control and monthly profit chasing/stop loss strategy";
                Name = "AGG6BarDailyMonthlyDrawdownLive3001";
                //Calculate = Calculate.OnEachTick; // don't need this, taken care of with AddDataSeries(Data.BarsPeriodType.Tick, 1);
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;           //only 1 position in each direction (long/short) at a time per strategy
                EntryHandling = EntryHandling.AllEntries; //the above restriction applies to all entries regardless of their naming, e.g. EnterLong(LotSize, "Long"), Long is the naming;
                IsExitOnSessionCloseStrategy = true;  //all positions (if still open) will be closed at session close
                ExitOnSessionCloseSeconds = 30; //The number of seconds before the actual session end time that the "IsExitOnSessionCloseStrategy" function will trigger.
                IsFillLimitOnTouch = false; //Determines if the strategy will use a more liberal fill algorithm for back-testing purposes only
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix; //When using MaximumBarsLookBack.TwoHundredFiftySix, only the last 256 values of the series object will be stored in memory and be accessible for reference
                OrderFillResolution = OrderFillResolution.Standard; //Determines how strategy orders are filled during historical states. Backtesting purpose
                Slippage = 0; //Sets the amount of slippage in ticks per execution used in performance calculations during backtests.
                /*
                 * When network goes down and subsequently reconnected, the following behaviors are expected:
                 * - If the Account Position is flat already, no reconciliatory order will be submitted. 
                 *      The strategy will then wait for the Strategy Position to reach a flat state as well before submitting any orders live.
                 * - If the Account Position is not flat, NinjaTrader will submit a market order(s) to reconcile the Account Position to a flat state. 
                 *      The strategy will then wait for the Strategy Position to reach a flat state before submitting live orders.
                 *   
                 *   The outcome is that NT strategy code will ensure all virtual positions to be flatten, and NT platform will ensure all account positions to be flatten.
                 */
                StartBehavior = StartBehavior.WaitUntilFlatSynchronizeAccount; 
                
                TimeInForce = TimeInForce.Gtc; //Sets the time in force property for all orders generated by a strategy. Order will remain working until the order is explicitly cancelled.
                
                //Determines if OnOrderTrace() would be called for a given strategy.  When enabled, traces are generated and displayed in the NinjaScript Output window for each call of an order method providing confirmation that the method is entered and providing information if order methods are ignored and why. 
                //This is valuable for debugging if you are not seeing expected behavior when calling an order method. 
                TraceOrders = true;

                //Defines the behavior of a strategy when a strategy generated order is returned from the broker's server in a "Rejected" state.
                //RealtimeErrorHandling.StopCancelClose is the default behavior, it will stop the strategy and cancel the order
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                
                //Determines how stop and target orders are submitted during an entry order execution. 
                //StopTargetHandling.ByStrategyPosition means Stop and Target order quantities will match the current strategy position.  (Stops and targets may result in "stacked" orders on partial fills)
                //If you would prefer all of your stops and targets to be placed at the same time within the same order, it is suggested to use StopTargetHandling.ByStrategyPosition. 
                //However this may result in more stop and target orders being submitted than the overall strategy position in a scenario in which the strategy's entire entry orders are not filled in one fill.
                StopTargetHandling = StopTargetHandling.ByStrategyPosition;
                
                BarsRequiredToTrade = 0; //The number of historical bars required before the strategy starts processing order methods called in the OnBarUpdate() method.

                //Sets the manner in which your strategy will behave when a connection loss is detected.
                //When using ConnectionLossHandling.Recalculate, recalculations will only occur if the strategy was stopped based on the conditions below.
                //•If data feed disconnects for longer than the time specified in DisconnectDelaySeconds, currently set at 10 secs, the strategy is stopped.
                //•If the order feed disconnects and the strategy places an order action while disconnected, the strategy is stopped.
                //•If both the data and order feeds disconnect for longer than the time specified in DisconnectDelaySeconds, currently set at 10 secs, the strategy is stopped.
                //Strategies will attempt to recalculate its strategy position when a connection is reestablished.
                ConnectionLossHandling = ConnectionLossHandling.Recalculate;

            }
            else if (State == State.Configure)
            {
                /* Add a secondary bar series.
                   Very Important: This secondary bar series needs to be smaller than the primary bar series.

                   Note: The primary bar series is whatever you choose for the strategy at startup.
                   In our case it is a 2000 ticks bar. */
                AddDataSeries(Data.BarsPeriodType.Tick, 1);

                // Add daily VIX data series
                AddDataSeries("^VIX", BarsPeriodType.Day, 1);

                // set static profit target and stop loss, this will ensure outstanding Account Positions are protected automatically
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

                // start real time mode with lineNo=0 for AI server
                lineNo = 0;

                // Read the current capital file .cc for the current capital, create one if it does not exist
                ReadCurrentCapital();

                // Read the 10 days EMA VIX from the VIX file to set up drawdown control settings 
                ReadEMAVixToSetUpDrawdownSettings();

                // This statement needs to be the last statement in real time state so that maxConsecutiveDailyLosses is set after maxConsecutiveLosses is set
                SetDailyWinLossState();
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
            // Necessary to call in order to clean up resources used by the StreamWriter object
            else if (State == State.Terminated)
            {
                if (swLog!=null)
                {
                    swLog.Close();
                    swLog.Dispose();
                    swLog = null;
                }

                if (swErr!=null)
                {
                    swErr.Close();
                    swErr.Dispose();
                    swErr = null;
                }

                if (swCC != null)
                {
                    swCC.Close();
                    swCC.Dispose();
                    swCC = null;
                }

                // Release the socket  
                if (sender!=null)
                {
                    sender.Shutdown(SocketShutdown.Both);
                    sender.Close();
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

                if (order.OrderState == OrderState.Filled)
                {
                    if (order.Filled == LotSize)
                    {
                        closedPrice = order.AverageFillPrice;
                        if (order.Name == "Long")
                            currPos = Position.posLong;
                        if (order.Name == "Short")
                            currPos = Position.posShort;

                        MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " #######Order filled=" + order.Filled + " closedPrice=" + closedPrice + " order name=" + order.Name + " currPos=" + currPos.ToString());
                    }
                    else
                        MyErrPrint(ErrorType.fatal, "Order filled, but not filled at lot size specified!!");
                }

                if (order.OrderState == OrderState.PartFilled)
                {
                    sumFilled = order.Filled;
                    orderPartialFilled = true;
                }

                // Reset the entryOrder object to null if order was cancelled without any fill
                if (order.OrderState == OrderState.Cancelled && order.Filled == 0)
                {
                    entryOrder = null;
                    sumFilled = 0;
                }

                // Report error and flatten position if new order submissoin rejected, fatal error if closing position rejected
                if (order.OrderState == OrderState.Rejected)
                {
                    if (attemptToFlattenPos) // attempting to close existing positions
                    {
                        MyErrPrint(ErrorType.fatal, "Closing position order rejected!! Check order status, may need to call brokerage to close existing position." + " ####### order filled=" + order.Filled);
                    } 
                    else // opening new position rejected
                    {
                        MyErrPrint(ErrorType.warning, "New position order rejected!!" + " ####### order filled=" + order.Filled);
                        
                        FlattenVirtualPositions(false);    // this will flatten virtual positions and reset all flags
                    }
                }
            }
        }



        // WARNING!!!! Will NOT receive position updates for manually placed orders, or orders managed by other strategies
        protected override void OnPositionUpdate(Cbi.Position position, double averagePrice,
            int quantity, Cbi.MarketPosition marketPosition)
        {
            if (position.MarketPosition == MarketPosition.Flat)
            {
                MyPrint("%%%%%%%%%%%%%%%%%%%%%%%% Account Positions: Flatten %%%%%%%%%%%%%%%%%%%%%%%%");
                MyPrint("@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@");
                MyPrint("PnL of last trade= " + SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1].ProfitCurrency);
                MyPrint("@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@");

                // current capital is accurately accounted for when the position is flatten
                currentCapital += SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1].ProfitCurrency;
                
                PrintProfitLossCurrentCapital();   // output current capital to cc file
                FlattenVirtualPositions(false);    // this will flatten virtual positions and reset all flags
            }
            if (position.MarketPosition == MarketPosition.Long)
            {
                MyPrint("%%%%%%%%%%%%%%%%%%%%%%%% Account Positions: Long %%%%%%%%%%%%%%%%%%%%%%%%");
            }
            if (position.MarketPosition == MarketPosition.Short)
            {
                MyPrint("%%%%%%%%%%%%%%%%%%%%%%%% Account Positions: Short %%%%%%%%%%%%%%%%%%%%%%%%");
            }
        }


        // Read the current capital file .cc for the current capital, create one if it does not exist
        private void ReadCurrentCapital()
        {
            // read the current capital file .cc for the current capital, create one if it does not exist
            if (swCC == null)
            {
                //Create file in the portNumber.cc format, the Path to current capital file, cc file does not have date as part of file name
                pathCC = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
                pathCC = System.IO.Path.Combine(pathCC, portNumber.ToString() + ".cc");

                if (File.Exists(pathCC))
                {
                    // Read current capital from the cc file
                    string ccStr = File.ReadAllText(pathCC);
                    currentCapital = Convert.ToDouble(ccStr);

                    // initializing the monthly control strategy variables with currentCapital from the cc file
                    yesterdayCapital = currentCapital; // keep track of capital from previous day
                    monthlyProfitChasingFlag = false; // set to false before the month
                }
                swCC = File.CreateText(pathCC); // Open the path for current capital
                swCC.WriteLine(currentCapital); // write current capital to cc file, if no existing file, InitStartingCapital will be written as currentCapital
            }
        }


        // Read the 10 days EMA VIX from the VIX file to set up drawdown control settings 
        private void ReadEMAVixToSetUpDrawdownSettings()
        {
            if (swVIX == null)
            {
                //Read file in the portNumber.cc format, the Path to current vix file, vix file does not have date as part of file name
                pathVIX = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
                pathVIX = System.IO.Path.Combine(pathVIX, portNumber.ToString() + ".vix");

                if (File.Exists(pathVIX))
                {
                    double currentVIX;

                    string maVIX = File.ReadAllText(pathVIX); // read moving average of VIX
                    currentVIX = Convert.ToDouble(maVIX);

                    // Set monthly and daily drawdown control strategy settings according to moving average VIX read from vix file
                    if (currentVIX >= HighVixTreshold)
                    {
                        maxConsecutiveLossesUpper = HVmaxConsecutiveLossesUpper;
                        maxConsecutiveLosses = HVmaxConsecutiveLossesUpper;
                        minConsecutiveWins = HVmaxConsecutiveLossesUpper;

                        profitChasingTarget = HVprofitChasingTarget; // % monthly gain profit target
                        maxPercentAllowableDrawdown = HVmaxPercentAllowableDrawdown; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
                        profitChasingAllowableDrawdown = HVprofitChasingAllowableDrawdown;
                    }
                    else
                    {
                        maxConsecutiveLossesUpper = LVmaxConsecutiveLossesUpper;
                        maxConsecutiveLosses = LVmaxConsecutiveLossesUpper;
                        minConsecutiveWins = LVmaxConsecutiveLossesUpper;

                        profitChasingTarget = LVprofitChasingTarget; // % monthly gain profit target
                        maxPercentAllowableDrawdown = LVmaxPercentAllowableDrawdown; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
                        profitChasingAllowableDrawdown = LVprofitChasingAllowableDrawdown;
                    }
                }
                else
                {
                    MyErrPrint(ErrorType.warning, pathVIX + " VIX file does not exist! Assume Low VIX drawdown control strategy settings.");

                    maxConsecutiveLossesUpper = LVmaxConsecutiveLossesUpper;
                    maxConsecutiveLosses = LVmaxConsecutiveLossesUpper;
                    minConsecutiveWins = LVmaxConsecutiveLossesUpper;

                    profitChasingTarget = LVprofitChasingTarget; // % monthly gain profit target
                    maxPercentAllowableDrawdown = LVmaxPercentAllowableDrawdown; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
                    profitChasingAllowableDrawdown = LVprofitChasingAllowableDrawdown;
                }
            }
        }

        private void MyErrPrint(ErrorType errType, string buf)
        {
            string errString="";

            //Set this scripts MyPrint() calls to the first output tab
            PrintTo = PrintTo.OutputTab1;

            if (swErr == null)
            {
                pathErr = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
                pathErr = System.IO.Path.Combine(pathErr, portNumber.ToString() + "-" + Time[0].ToString("yyyyMMdd") + ".err");
                swErr = File.CreateText(pathErr);  // Open the path for err file writing
            }

            switch (errType)
            {
                case ErrorType.fatal:
                    errString = "FATAL: ";
                    haltTrading = true;
                    break;
                case ErrorType.warning:
                    errString = "WARNING: ";
                    break;
            }

            swErr.WriteLine(errString + Time[0].ToString("yyyyMMdd:HHmmss: ") + buf); // Append a new line to the err file

            // close error file
            swErr.Close();
            swErr.Dispose();
            swErr = null;

            Print(errString + Time[0].ToString("yyyyMMdd:HHmmss: ") + buf); // Screen print out
            MyPrint(errString + buf); // replicate error message to log file

            // Cancels all working orders, closes any existing positions, and finally disables the strategy. 
            if (errType == ErrorType.fatal)
            {
                CloseStrategy(buf);
            }
        }

        private void MyPrint(string buf)
        {
            //Set this scripts MyPrint() calls to the second output tab
            PrintTo = PrintTo.OutputTab2;

            if (swLog == null)
            {
                //Create log file in the portNumber-yyyyMMdd.log format
                pathLog = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
                pathLog = System.IO.Path.Combine(pathLog, portNumber.ToString() + "-" + Time[0].ToString("yyyyMMdd") + ".log");

                swLog = File.AppendText(pathLog);  // Open the path for log file writing
            }

            swLog.WriteLine(Time[0].ToString("yyyyMMdd:HHmmss: ") + buf); // Append a new line to the log file
            Print(Time[0].ToString("yyyyMMdd:HHmmss: ") + buf);
        }

        // Account for daily wins and losses for daily drawdown control
        private void SetDailyWinLossState()
        {
            maxConsecutiveDailyLosses = maxConsecutiveLosses;
            consecutiveDailyLosses = 0;
            consecutiveDailyWins = 0;
        }

        // increment of daily win will increase max consecutive daily losses as long as it does not exceed the upper limit, 
        // it will also reset consecutive daily losses back to zero
        private void IncrementDailyWin()
        {
            consecutiveDailyWins++;
            consecutiveDailyLosses = 0;

            if (consecutiveDailyWins >= minConsecutiveWins)
            {
                if (maxConsecutiveDailyLosses < maxConsecutiveLossesUpper)
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
            EnterShort(LotSize, "Short");
            MyPrint("Server Signal=" + svrSignal + " Short");
        }

        private void AiLong()
        {
            EnterLong(LotSize, "Long");
            MyPrint("Server Signal=" + svrSignal + " Long");
        }

        private void FlattenVirtualPositions(bool stopLoss)
        {
            currPos = Position.posFlat;
            profitChasingFlag = false;
            stopLossEncountered = stopLoss;
            sumFilled = 0;
            orderPartialFilled = false;
            attemptToFlattenPos = false;
        }

        
        private void AiFlat()
        {
            MyPrint("AiFlat: currPos = " + currPos.ToString());

            if (!PosFlat())
            {
                // attempting to flatten virtual positions, flattening of positions will take place in OnPositionUpdate() callback
                attemptToFlattenPos = true;

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
            }
        }

        private void StartNewTradePosition(string signal)
        {
            //MyPrint("StartNewTradePosition");
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
            // don't start new trade if halt trading or attempting to flatten positions
            if (haltTrading || attemptToFlattenPos)
                return;

            // don't execute trade if consecutive losses greater than allowable limits
            if (consecutiveDailyLosses >= maxConsecutiveDailyLosses)
            {
                MyPrint("consecutiveDailyLosses >= maxConsecutiveDailyLosses, Skipping StartNewTradePosition");
                return;
            }

            // Set monthlyProfitChasingFlag, once monthlyProfitChasingFlag sets to true, it will stay true until end of the month
            if (!monthlyProfitChasingFlag)
            {
                if (currentCapital > (InitStartingCapital * (1 + profitChasingTarget)))
                {
                    monthlyProfitChasingFlag = true;
                    MyPrint("$$$$$$$$$$$$$ Monthly profit target met, Monthly Profit Chasing and Stop Loss begins! $$$$$$$$$$$$$");
                }
            }

            // Don't trade if monthly profit chasing and stop loss strategy decided not to trade for the rest of the month
            if (monthlyProfitChasingFlag)
            {
                // trading halt if suffers more than profitChasingAllowableDrawdown losses from yesterdayCapital
                if (currentCapital < (yesterdayCapital * (1 - profitChasingAllowableDrawdown)))
                {
                    MyPrint("$$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                    haltTrading = true;
                    return;
                }
            }
            else
            {
                // trading halt if suffers more than maxPercentAllowableDrawdown losses
                if (currentCapital < (InitStartingCapital * (1 - maxPercentAllowableDrawdown)))
                {
                    MyPrint("!!!!!!!!!!!! Monthly profit target NOT met, stop loss enforced, Skipping StartNewTradePosition !!!!!!!!!!!!");
                    haltTrading = true;
                    return;
                }
            }

            //MyPrint("ExecuteAITrade");
            if (PosFlat())
            {
                StartNewTradePosition(signal);
                return;
            }
        }

        private void HandleSoftDeck(string signal)
        {
            double estCurrentCapital;

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
                    // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                    estCurrentCapital = currentCapital + ((Close[0] - closedPrice) * 50 - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (estCurrentCapital < yesterdayCapital))
                    {
                        MyPrint("estCurrentCapital=" + estCurrentCapital.ToString() + " yesterdayCapital=" + yesterdayCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
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
                    // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                    estCurrentCapital = currentCapital + ((closedPrice - Close[0]) * 50 - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (estCurrentCapital < yesterdayCapital))
                    {
                        MyPrint("estCurrentCapital=" + estCurrentCapital.ToString() + " yesterdayCapital=" + yesterdayCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
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
            double estCurrentCapital;

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
                // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                estCurrentCapital = currentCapital + ((Close[0] - closedPrice) * 50 - CommissionRate);

                // stop trading if monthly profit is met and trading going negative
                if (monthlyProfitChasingFlag && (estCurrentCapital < yesterdayCapital))
                {
                    MyPrint("estCurrentCapital=" + estCurrentCapital.ToString() + " yesterdayCapital=" + yesterdayCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                    haltTrading = true;
                }
            }

            if (PosShort())
            {
                MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleHardDeck:: " + " OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0] + " @@@@@ L O S E R @@@@@@ loss= " + ((closedPrice - Close[0]) * 50 - CommissionRate).ToString());
                AiFlat();

                IncrementDailyLoss();

                // keeping records for monthly profit chasing and stop loss strategy
                // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                estCurrentCapital = currentCapital + ((closedPrice - Close[0]) * 50 - CommissionRate);

                // stop trading if monthly profit is met and trading going negative
                if (monthlyProfitChasingFlag && (estCurrentCapital < yesterdayCapital))
                {
                    MyPrint("estCurrentCapital=" + estCurrentCapital.ToString() + " yesterdayCapital=" + yesterdayCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
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
            double estCurrentCapital;

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
                    // currentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                    estCurrentCapital = currentCapital + ((Close[0] - closedPrice) * 50 - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (estCurrentCapital < yesterdayCapital))
                    {
                        MyPrint("estCurrentCapital=" + estCurrentCapital.ToString() + " yesterdayCapital=" + yesterdayCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
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
                    // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                    estCurrentCapital = currentCapital + ((closedPrice - Close[0]) * 50 - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (estCurrentCapital < yesterdayCapital))
                    {
                        MyPrint("estCurrentCapital=" + estCurrentCapital.ToString() + " yesterdayCapital=" + yesterdayCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
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
            double estCurrentCapital;

            // EOD close current position(s)
            if (PosLong())
            {
                MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleEOD:: " + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " Close[0]=" + Close[0].ToString() + " P/L= " + ((Close[0] - closedPrice) * 50 - CommissionRate).ToString());

                AiFlat();

                // keeping records for monthly profit chasing and stop loss strategy
                // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                estCurrentCapital = currentCapital + ((Close[0] - closedPrice) * 50 - CommissionRate);

                // stop trading if monthly profit is met and trading going negative
                if (monthlyProfitChasingFlag && (estCurrentCapital < yesterdayCapital))
                {
                    MyPrint("estCurrentCapital=" + estCurrentCapital.ToString() + " yesterdayCapital=" + yesterdayCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                    haltTrading = true;
                }
                return;
            }

            if (PosShort())
            {
                MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleEOD:: " + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " Close[0]=" + Close[0].ToString() + " P/L= " + ((closedPrice - Close[0]) * 50 - CommissionRate).ToString());

                AiFlat();

                // keeping records for monthly profit chasing and stop loss strategy
                // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                estCurrentCapital = currentCapital + ((closedPrice - Close[0]) * 50 - CommissionRate);

                // stop trading if monthly profit is met and trading going negative
                if (monthlyProfitChasingFlag && (estCurrentCapital < yesterdayCapital))
                {
                    MyPrint("estCurrentCapital=" + estCurrentCapital.ToString() + " yesterdayCapital=" + yesterdayCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
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

            // this will flatten virtual positions and reset all flags
            FlattenVirtualPositions(false);
            lineNo = 0;
        }

        private void PrintProfitLossCurrentCapital()
        {
            double cumulativePL = SystemPerformance.AllTrades.TradesPerformance.NetProfit; // cumulative P&L

            // MyPrint out the net profit of all trades
            MyPrint("$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
            MyPrint("Cumulative net profit is: " + cumulativePL);
            MyPrint("$$$$$$$$$$$$$$$$$$$$$$$$$$$$");

            // MyPrint out the current capital with P/L
            MyPrint("$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
            MyPrint("Curret capital is: " + currentCapital);
            MyPrint("$$$$$$$$$$$$$$$$$$$$$$$$$$$$");

            // ouput current capital to cc file
            swCC.WriteLine(currentCapital);
        }

        // Need to Handle end of session on tick because to avoid closing position past current day
        private void HandleEndOfSession()
        {
            DateTime endSessionTime;

            yesterdayCapital = currentCapital;

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

                    SetDailyWinLossState();
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
                if (State != State.Realtime)
                {
                    // during live trading, flatten all virtual positions when loading historical data, real time trading will start with flat position
                    // See StartBehavior = StartBehavior.WaitUntilFlatSynchronizeAccount; 
                    if (!PosFlat())
                        FlattenVirtualPositions(false); // this will flatten virtual positions and reset all flags

                    // reset lineNo to 0 for all other states, real time trading will start with lineNo = 0
                    lineNo = 0;
                    return;
                }

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
                    MyErrPrint(ErrorType.fatal, "Socket exception::" + ex.Message + "" + ex.ToString());
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
                    if (PosFlat())
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
            else if (BarsInProgress == 1)
            {
                // Need to Handle end of session on tick because to avoid closing position past current day
                HandleEndOfSession();

                // HandleEndOfSession would close all positions
                if (!endSession && ViolateHardDeck())
                {
                    HandleHardDeck();

                    // this will flatten virtual positions and reset all flags, stopLoss = true
                    FlattenVirtualPositions(true);
                }
                return;
            }
            // ^VIX daily data
            else if (BarsInProgress == 2)
            {
                MyPrint("======================================================");
                MyPrint("^VIX 10 days EMA " + EMA(BarsArray[2], 10)[0]);
                MyPrint("======================================================");

                // write 10 days EMA VIX into VIX file 
                swVIX.WriteLine(EMA(BarsArray[2], 10)[0]);
            }
        }
    }
}
